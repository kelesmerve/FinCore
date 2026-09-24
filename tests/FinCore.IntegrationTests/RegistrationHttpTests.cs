using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinCore.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FinCore.IntegrationTests;

public class RegistrationHttpTests
{
    private const string Password = "Http-test-password42!";

    [Fact]
    public async Task Register_WithValidRequest_Returns201AndPersistsUserAndAccount()
    {
        var email = $"http-{Guid.NewGuid():N}@example.com";
        using var factory = new RegistrationFactory();
        using var client = factory.CreateClient();
        try
        {
            using var response = await client.PostAsJsonAsync("/api/auth/register",
                new { Email = $"  {email.ToUpperInvariant()}  ", Password });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            using var json = JsonDocument.Parse(body);
            var result = json.RootElement;
            Assert.Equal(new[] { "accountId", "accountNumber", "email", "userId" },
                result.EnumerateObject().Select(property => property.Name).OrderBy(name => name));
            Assert.DoesNotContain(Password, body);
            Assert.Equal(email, result.GetProperty("email").GetString());

            await using var context = factory.CreateDbContext();
            var user = await context.Users.SingleAsync(item => item.Email == email);
            var account = await context.Accounts.SingleAsync(item => item.UserId == user.Id);
            Assert.Equal(user.Id, result.GetProperty("userId").GetGuid());
            Assert.Equal(account.Id, result.GetProperty("accountId").GetGuid());
            Assert.Equal(account.AccountNumber, result.GetProperty("accountNumber").GetString());
            Assert.DoesNotContain(user.PasswordHash, body);
        }
        finally
        {
            await factory.CleanupAsync(email);
        }
    }

    [Fact]
    public async Task Register_WithDuplicateEmail_Returns409ProblemDetails()
    {
        var email = $"http-{Guid.NewGuid():N}@example.com";
        using var factory = new RegistrationFactory();
        using var client = factory.CreateClient();
        try
        {
            using var first = await client.PostAsJsonAsync("/api/auth/register", new { Email = email, Password });
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);
            using var second = await client.PostAsJsonAsync("/api/auth/register", new { Email = email, Password });
            Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
            Assert.Equal("application/problem+json", second.Content.Headers.ContentType?.MediaType);
            using var json = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
            Assert.Equal(409, json.RootElement.GetProperty("status").GetInt32());
            await using var context = factory.CreateDbContext();
            Assert.Equal(1, await context.Users.CountAsync(user => user.Email == email));
        }
        finally
        {
            await factory.CleanupAsync(email);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Register_WithInvalidEmailOrWeakPassword_Returns400ProblemDetails(bool invalidEmail)
    {
        var email = invalidEmail ? $"invalid-{Guid.NewGuid():N}" : $"http-{Guid.NewGuid():N}@example.com";
        using var factory = new RegistrationFactory();
        using var client = factory.CreateClient();
        try
        {
            using var response = await client.PostAsJsonAsync("/api/auth/register",
                new { Email = email, Password = invalidEmail ? Password : "weak" });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(400, json.RootElement.GetProperty("status").GetInt32());
            await using var context = factory.CreateDbContext();
            Assert.False(await context.Users.AnyAsync(user => user.Email == email));
        }
        finally
        {
            await factory.CleanupAsync(email);
        }
    }

    private sealed class RegistrationFactory : WebApplicationFactory<Program>
    {
        private readonly string _connectionString;

        public RegistrationFactory()
        {
            var configuration = new ConfigurationBuilder()
                .AddUserSecrets<RegistrationHttpTests>(optional: true)
                .AddEnvironmentVariables()
                .Build();
            _connectionString = configuration.GetConnectionString("FinCoreDatabase")
                ?? throw new InvalidOperationException("Configure ConnectionStrings:FinCoreDatabase for HTTP tests.");
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:FinCoreDatabase", _connectionString);
        }

        public FinCoreDbContext CreateDbContext() => new(
            new DbContextOptionsBuilder<FinCoreDbContext>().UseNpgsql(_connectionString).Options);

        public async Task CleanupAsync(string email)
        {
            await using var context = CreateDbContext();
            await using var transaction = await context.Database.BeginTransactionAsync();
            var userIds = await context.Users.Where(user => user.Email == email)
                .Select(user => user.Id).ToArrayAsync();
            await context.Accounts.Where(account => userIds.Contains(account.UserId)).ExecuteDeleteAsync();
            await context.Users.Where(user => userIds.Contains(user.Id)).ExecuteDeleteAsync();
            await transaction.CommitAsync();
        }
    }
}
