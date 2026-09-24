using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinCore.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FinCore.IntegrationTests;

public class LoginHttpTests
{
    private const string Password = "Login-test-password42!";

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsTokenThatAuthenticatesMe()
    {
        using var factory = new LoginFactory();
        using var client = factory.CreateClient();
        var email = $"login-{Guid.NewGuid():N}@example.com";
        try
        {
            await RegisterAsync(client, email);
            using var response = await client.PostAsJsonAsync("/api/auth/login",
                new { Email = $" {email.ToUpperInvariant()} ", Password });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(new[] { "accessToken", "expiresAtUtc" },
                json.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n));
            var token = json.RootElement.GetProperty("accessToken").GetString();
            Assert.False(string.IsNullOrWhiteSpace(token));
            Assert.InRange(json.RootElement.GetProperty("expiresAtUtc").GetDateTime(),
                DateTime.UtcNow.AddMinutes(14), DateTime.UtcNow.AddMinutes(16));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var me = await client.GetAsync("/api/auth/me");
            Assert.Equal(HttpStatusCode.OK, me.StatusCode);
            using var identity = JsonDocument.Parse(await me.Content.ReadAsStringAsync());
            await using var context = factory.CreateDbContext();
            var user = await context.Users.SingleAsync(u => u.Email == email);
            Assert.Equal(new[] { "email", "role", "userId" },
                identity.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n));
            Assert.Equal(user.Id, identity.RootElement.GetProperty("userId").GetGuid());
            Assert.Equal(email, identity.RootElement.GetProperty("email").GetString());
            Assert.Equal("Customer", identity.RootElement.GetProperty("role").GetString());
        }
        finally { await factory.CleanupAsync(email); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Me_WithoutValidToken_Returns401(bool invalidToken)
    {
        using var factory = new LoginFactory();
        using var client = factory.CreateClient();
        if (invalidToken)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid-token");
        using var response = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_WrongPasswordAndMissingEmail_ReturnSameSafe401Problem()
    {
        using var factory = new LoginFactory();
        using var client = factory.CreateClient();
        var email = $"login-{Guid.NewGuid():N}@example.com";
        try
        {
            await RegisterAsync(client, email);
            using var wrong = await client.PostAsJsonAsync("/api/auth/login",
                new { Email = email, Password = "Wrong-password42!" });
            using var missing = await client.PostAsJsonAsync("/api/auth/login",
                new { Email = $"missing-{Guid.NewGuid():N}@example.com", Password });
            await AssertProblemAsync(wrong, HttpStatusCode.Unauthorized);
            await AssertProblemAsync(missing, HttpStatusCode.Unauthorized);
            using var first = JsonDocument.Parse(await wrong.Content.ReadAsStringAsync());
            using var second = JsonDocument.Parse(await missing.Content.ReadAsStringAsync());
            Assert.Equal(first.RootElement.GetProperty("title").GetString(),
                second.RootElement.GetProperty("title").GetString());
            Assert.Equal(first.RootElement.GetProperty("type").GetString(),
                second.RootElement.GetProperty("type").GetString());
            Assert.Equal(first.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n),
                second.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n));
            Assert.False(first.RootElement.TryGetProperty("detail", out _));
            Assert.False(second.RootElement.TryGetProperty("detail", out _));
        }
        finally { await factory.CleanupAsync(email); }
    }

    [Fact]
    public async Task Login_InactiveUser_Returns403OnlyWithCorrectPassword()
    {
        using var factory = new LoginFactory();
        using var client = factory.CreateClient();
        var email = $"login-{Guid.NewGuid():N}@example.com";
        try
        {
            await RegisterAsync(client, email);
            await using (var context = factory.CreateDbContext())
            {
                var user = await context.Users.SingleAsync(u => u.Email == email);
                user.Deactivate();
                await context.SaveChangesAsync();
            }
            using var wrong = await client.PostAsJsonAsync("/api/auth/login",
                new { Email = email, Password = "Wrong-password42!" });
            await AssertProblemAsync(wrong, HttpStatusCode.Unauthorized);
            using var correct = await client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password });
            await AssertProblemAsync(correct, HttpStatusCode.Forbidden);
        }
        finally { await factory.CleanupAsync(email); }
    }

    [Theory]
    [InlineData("", Password)]
    [InlineData("   ", Password)]
    [InlineData("invalid-email", Password)]
    [InlineData("valid@example.com", "")]
    [InlineData("valid@example.com", "   ")]
    [InlineData(null, Password)]
    [InlineData("valid@example.com", null)]
    public async Task Login_InvalidInput_Returns400(string? email, string? password)
    {
        using var factory = new LoginFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        await AssertProblemAsync(response, HttpStatusCode.BadRequest);
    }

    private static async Task RegisterAsync(HttpClient client, string email)
    {
        using var response = await client.PostAsJsonAsync("/api/auth/register", new { Email = email, Password });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal((int)status, json.RootElement.GetProperty("status").GetInt32());
        Assert.False(json.RootElement.TryGetProperty("accessToken", out _));
        Assert.False(json.RootElement.TryGetProperty("password", out _));
        Assert.False(json.RootElement.TryGetProperty("passwordHash", out _));
    }

    private sealed class LoginFactory : WebApplicationFactory<Program>
    {
        private readonly string _connectionString = new ConfigurationBuilder()
            .AddUserSecrets<LoginHttpTests>(optional: true).AddEnvironmentVariables().Build()
            .GetConnectionString("FinCoreDatabase")
            ?? throw new InvalidOperationException("Configure ConnectionStrings:FinCoreDatabase for HTTP tests.");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddUserSecrets<LoginHttpTests>(optional: true).AddEnvironmentVariables());
            builder.UseSetting("ConnectionStrings:FinCoreDatabase", _connectionString);
        }

        public FinCoreDbContext CreateDbContext() => new(
            new DbContextOptionsBuilder<FinCoreDbContext>().UseNpgsql(_connectionString).Options);

        public async Task CleanupAsync(string email)
        {
            await using var context = CreateDbContext();
            await using var transaction = await context.Database.BeginTransactionAsync();
            var ids = await context.Users.Where(u => u.Email == email).Select(u => u.Id).ToArrayAsync();
            await context.Accounts.Where(a => ids.Contains(a.UserId)).ExecuteDeleteAsync();
            await context.Users.Where(u => ids.Contains(u.Id)).ExecuteDeleteAsync();
            await transaction.CommitAsync();
        }
    }
}
