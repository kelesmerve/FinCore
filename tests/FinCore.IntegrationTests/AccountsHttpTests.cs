using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
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

public sealed class AccountsHttpTests
{
    [Fact]
    public async Task GetMine_WithoutJwt_Returns401()
    {
        using var factory = new AccountFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/accounts/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetMine_InvalidSubject_ReturnsSafe401()
    {
        using var factory = new AccountFactory();
        using var client = factory.CreateClient();
        factory.Authenticate(client, "not-a-guid");

        using var response = await client.GetAsync("/api/accounts/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(401, json.RootElement.GetProperty("status").GetInt32());
        Assert.False(json.RootElement.TryGetProperty("detail", out _));
    }

    [Fact]
    public async Task GetMine_ReturnsOnlyOwnAccountsWithSafeFieldsAndCorrectValues()
    {
        using var factory = new AccountFactory();
        var ownUser = User.CreateCustomer($"accounts-{Guid.NewGuid():N}@example.com", "test-hash");
        var otherUser = User.CreateCustomer($"accounts-{Guid.NewGuid():N}@example.com", "test-hash");
        var first = new Account(ownUser.Id, Guid.NewGuid().ToString("N"));
        var second = new Account(ownUser.Id, Guid.NewGuid().ToString("N"));
        var other = new Account(otherUser.Id, Guid.NewGuid().ToString("N"));
        first.Credit(new Money(12.50m));
        second.Deactivate();
        try
        {
            await using (var context = factory.CreateDbContext())
            {
                context.Users.AddRange(ownUser, otherUser);
                context.Accounts.AddRange(first, second, other);
                await context.SaveChangesAsync();
            }

            using var client = factory.CreateClient();
            factory.Authenticate(client, ownUser.Id.ToString());
            using var response = await client.GetAsync("/api/accounts/me");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("password", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("userId", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(other.AccountNumber, body);
            using var json = JsonDocument.Parse(body);
            var items = json.RootElement.EnumerateArray().ToArray();
            Assert.Equal(2, items.Length);
            await using var verify = factory.CreateDbContext();
            var expectedIds = await verify.Accounts.AsNoTracking()
                .Where(account => account.UserId == ownUser.Id)
                .OrderBy(account => account.CreatedAtUtc).ThenBy(account => account.Id)
                .Select(account => account.Id).ToArrayAsync();
            Assert.Equal(expectedIds, items.Select(item => item.GetProperty("id").GetGuid()));
            Assert.All(items, item => Assert.Equal(
                new[] { "accountNumber", "balance", "createdAtUtc", "currency", "id", "status" },
                item.EnumerateObject().Select(property => property.Name).OrderBy(name => name)));
            var firstItem = items.Single(item => item.GetProperty("id").GetGuid() == first.Id);
            var secondItem = items.Single(item => item.GetProperty("id").GetGuid() == second.Id);
            Assert.Equal(first.AccountNumber, firstItem.GetProperty("accountNumber").GetString());
            Assert.Equal(12.50m, firstItem.GetProperty("balance").GetDecimal());
            Assert.Equal("TRY", firstItem.GetProperty("currency").GetString());
            Assert.Equal("Active", firstItem.GetProperty("status").GetString());
            Assert.Equal("Inactive", secondItem.GetProperty("status").GetString());
        }
        finally { await factory.CleanupAsync(ownUser.Id, otherUser.Id); }
    }

    [Fact]
    public async Task GetMine_UserWithoutAccounts_Returns200AndEmptyList()
    {
        using var factory = new AccountFactory();
        var user = User.CreateCustomer($"accounts-{Guid.NewGuid():N}@example.com", "test-hash");
        try
        {
            await using (var context = factory.CreateDbContext())
            {
                context.Users.Add(user);
                await context.SaveChangesAsync();
            }
            using var client = factory.CreateClient();
            factory.Authenticate(client, user.Id.ToString());
            using var response = await client.GetAsync("/api/accounts/me");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Empty(json.RootElement.EnumerateArray());
        }
        finally { await factory.CleanupAsync(user.Id); }
    }

    private sealed class AccountFactory : WebApplicationFactory<Program>
    {
        private readonly string _connectionString = new ConfigurationBuilder()
            .AddUserSecrets<AccountsHttpTests>(optional: true).AddEnvironmentVariables().Build()
            .GetConnectionString("FinCoreDatabase")
            ?? throw new InvalidOperationException("Configure ConnectionStrings:FinCoreDatabase for HTTP tests.");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddUserSecrets<AccountsHttpTests>(optional: true).AddEnvironmentVariables());
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
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        }

        public async Task CleanupAsync(params Guid[] userIds)
        {
            await using var context = CreateDbContext();
            await using var transaction = await context.Database.BeginTransactionAsync();
            await context.Accounts.Where(account => userIds.Contains(account.UserId)).ExecuteDeleteAsync();
            await context.Users.Where(user => userIds.Contains(user.Id)).ExecuteDeleteAsync();
            await transaction.CommitAsync();
        }
    }
}
