using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FinCore.Domain.Entities;
using FinCore.Domain.ValueObjects;
using FinCore.Infrastructure.Persistence;
using FinCore.Infrastructure.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace FinCore.IntegrationTests;

public sealed class TransferHttpTests
{
    [Fact]
    public async Task Transfer_WithoutJwtOrValidSubject_Returns401()
    {
        using var factory = new TransferFactory();
        using var client = factory.CreateClient();
        using var anonymous = await client.PostAsJsonAsync("/api/transfers", new { SourceAccountId = Guid.NewGuid(), DestinationAccountId = Guid.NewGuid(), Amount = 1m });
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        factory.Authenticate(client, "not-a-guid");
        using var malformedSubject = await client.PostAsJsonAsync("/api/transfers", new { SourceAccountId = Guid.NewGuid(), DestinationAccountId = Guid.NewGuid(), Amount = 1m });
        await AssertProblemAsync(malformedSubject, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Transfer_OwnSourceToAnotherUser_PersistsBalancesAndSafeDoubleEntry()
    {
        using var factory = new TransferFactory();
        var data = await factory.SeedAsync();
        try
        {
            using var client = factory.CreateClient();
            factory.Authenticate(client, data.SourceUser.Id.ToString());
            using var response = await client.PostAsJsonAsync("/api/transfers", new
            {
                SourceAccountId = data.Source.Id, DestinationAccountId = data.Destination.Id, Amount = 25.25m
            });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            Assert.Equal(new[] { "amount", "createdAtUtc", "currency", "destinationAccountId", "sourceAccountId", "transactionId" },
                root.EnumerateObject().Select(p => p.Name).OrderBy(n => n));
            Assert.DoesNotContain("password", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("balance", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("user", body, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(25.25m, root.GetProperty("amount").GetDecimal());
            Assert.Equal("TRY", root.GetProperty("currency").GetString());
            Assert.Equal(data.Source.Id, root.GetProperty("sourceAccountId").GetGuid());
            Assert.Equal(data.Destination.Id, root.GetProperty("destinationAccountId").GetGuid());

            await using var verify = factory.CreateDbContext();
            Assert.Equal(new Money(74.75m), (await verify.Accounts.SingleAsync(a => a.Id == data.Source.Id)).Balance);
            Assert.Equal(new Money(25.25m), (await verify.Accounts.SingleAsync(a => a.Id == data.Destination.Id)).Balance);
            var ledger = await verify.LedgerTransactions.Include(t => t.Entries)
                .SingleAsync(t => t.Id == root.GetProperty("transactionId").GetGuid());
            Assert.Equal(2, ledger.Entries.Count);
            Assert.Contains(ledger.Entries, e => e.AccountId == data.Source.Id && e.Type == LedgerEntryType.Debit);
            Assert.Contains(ledger.Entries, e => e.AccountId == data.Destination.Id && e.Type == LedgerEntryType.Credit);
            Assert.All(ledger.Entries, e =>
            {
                Assert.Equal(ledger.Id, e.LedgerTransactionId);
                Assert.Equal(new Money(25.25m), e.Amount);
            });
        }
        finally { await factory.CleanupAsync(data); }
    }

    [Theory]
    [InlineData("foreign-source", HttpStatusCode.Forbidden)]
    [InlineData("zero", HttpStatusCode.BadRequest)]
    [InlineData("negative", HttpStatusCode.BadRequest)]
    [InlineData("fraction", HttpStatusCode.BadRequest)]
    [InlineData("same-account", HttpStatusCode.BadRequest)]
    [InlineData("missing-source", HttpStatusCode.NotFound)]
    [InlineData("missing-destination", HttpStatusCode.NotFound)]
    [InlineData("insufficient", HttpStatusCode.Conflict)]
    [InlineData("inactive-source", HttpStatusCode.Conflict)]
    [InlineData("inactive-destination", HttpStatusCode.Conflict)]
    public async Task Transfer_InvalidOrForbiddenRequest_DoesNotChangeBalancesOrLedger(string scenario, HttpStatusCode status)
    {
        using var factory = new TransferFactory();
        var data = await factory.SeedAsync();
        try
        {
            if (scenario is "inactive-source" or "inactive-destination")
            {
                await using var context = factory.CreateDbContext();
                var accountId = scenario == "inactive-source" ? data.Source.Id : data.Destination.Id;
                (await context.Accounts.SingleAsync(a => a.Id == accountId)).Deactivate();
                await context.SaveChangesAsync();
            }
            using var client = factory.CreateClient();
            factory.Authenticate(client, (scenario == "foreign-source" ? data.DestinationUser.Id : data.SourceUser.Id).ToString());
            var sourceId = scenario == "missing-source" ? Guid.NewGuid() : data.Source.Id;
            var destinationId = scenario switch
            {
                "same-account" => data.Source.Id,
                "missing-destination" => Guid.NewGuid(),
                _ => data.Destination.Id
            };
            var amount = scenario switch { "zero" => 0m, "negative" => -1m, "fraction" => 1.001m, "insufficient" => 101m, _ => 10m };
            using var response = await client.PostAsJsonAsync("/api/transfers", new
            {
                SourceAccountId = sourceId, DestinationAccountId = destinationId, Amount = amount
            });
            await AssertProblemAsync(response, status);
            await using var verify = factory.CreateDbContext();
            Assert.Equal(new Money(100m), (await verify.Accounts.SingleAsync(a => a.Id == data.Source.Id)).Balance);
            Assert.Equal(Money.Zero, (await verify.Accounts.SingleAsync(a => a.Id == data.Destination.Id)).Balance);
            Assert.False(await verify.LedgerTransactions.AnyAsync(t => t.SourceAccountId == data.Source.Id || t.DestinationAccountId == data.Destination.Id));
            Assert.False(await verify.LedgerEntries.AnyAsync(e => e.AccountId == data.Source.Id || e.AccountId == data.Destination.Id));
        }
        finally { await factory.CleanupAsync(data); }
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal((int)status, json.RootElement.GetProperty("status").GetInt32());
        Assert.False(json.RootElement.TryGetProperty("detail", out _));
        Assert.DoesNotContain("password", json.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    private sealed record Seed(User SourceUser, User DestinationUser, Account Source, Account Destination);

    private sealed class TransferFactory : WebApplicationFactory<Program>
    {
        private readonly string _connectionString = new ConfigurationBuilder()
            .AddUserSecrets<TransferHttpTests>(optional: true).AddEnvironmentVariables().Build()
            .GetConnectionString("FinCoreDatabase")
            ?? throw new InvalidOperationException("Configure ConnectionStrings:FinCoreDatabase for HTTP tests.");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddUserSecrets<TransferHttpTests>(optional: true).AddEnvironmentVariables());
            builder.UseSetting("ConnectionStrings:FinCoreDatabase", _connectionString);
        }

        public FinCoreDbContext CreateDbContext() => new(
            new DbContextOptionsBuilder<FinCoreDbContext>().UseNpgsql(_connectionString).Options);

        public void Authenticate(HttpClient client, string subject)
        {
            var options = Services.GetRequiredService<IOptions<JwtOptions>>().Value;
            var now = DateTime.UtcNow;
            var token = new JwtSecurityToken(options.Issuer, options.Audience,
                [new Claim("sub", subject), new Claim("role", "Customer"), new Claim("jti", Guid.NewGuid().ToString())],
                now, now.AddMinutes(5),
                new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SecretKey)), SecurityAlgorithms.HmacSha256));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        }

        public async Task<Seed> SeedAsync()
        {
            var sourceUser = User.CreateCustomer($"transfer-http-{Guid.NewGuid():N}@example.com", "test-hash");
            var destinationUser = User.CreateCustomer($"transfer-http-{Guid.NewGuid():N}@example.com", "test-hash");
            var source = new Account(sourceUser.Id, Guid.NewGuid().ToString("N"));
            var destination = new Account(destinationUser.Id, Guid.NewGuid().ToString("N"));
            source.Credit(new Money(100m));
            await using var context = CreateDbContext();
            context.Users.AddRange(sourceUser, destinationUser);
            context.Accounts.AddRange(source, destination);
            await context.SaveChangesAsync();
            return new Seed(sourceUser, destinationUser, source, destination);
        }

        public async Task CleanupAsync(Seed data)
        {
            await using var context = CreateDbContext();
            await using var transaction = await context.Database.BeginTransactionAsync();
            var accountIds = new[] { data.Source.Id, data.Destination.Id };
            var transactionIds = await context.LedgerTransactions
                .Where(t => accountIds.Contains(t.SourceAccountId) && accountIds.Contains(t.DestinationAccountId))
                .Select(t => t.Id).ToArrayAsync();
            await context.LedgerEntries.Where(e => transactionIds.Contains(e.LedgerTransactionId)).ExecuteDeleteAsync();
            await context.LedgerTransactions.Where(t => transactionIds.Contains(t.Id)).ExecuteDeleteAsync();
            await context.Accounts.Where(a => accountIds.Contains(a.Id)).ExecuteDeleteAsync();
            var userIds = new[] { data.SourceUser.Id, data.DestinationUser.Id };
            await context.Users.Where(u => userIds.Contains(u.Id)).ExecuteDeleteAsync();
            await transaction.CommitAsync();
        }
    }
}
