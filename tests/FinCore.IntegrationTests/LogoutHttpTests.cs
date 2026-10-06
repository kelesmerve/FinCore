using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinCore.Infrastructure.Persistence;
using FinCore.Infrastructure.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FinCore.IntegrationTests;

public class LogoutHttpTests
{
    private const string Password = "Logout-test-password42!";

    [Fact]
    public async Task Logout_AfterLogin_IsIdempotentAndPreventsRefresh()
    {
        using var factory = new LogoutFactory();
        using var client = factory.CreateClient();
        var email = $"logout-{Guid.NewGuid():N}@example.com";
        try
        {
            await RegisterAsync(client, email);
            var raw = await LoginAsync(client, email);
            using var first = await client.PostAsJsonAsync("/api/auth/logout", new { RefreshToken = raw });
            using var second = await client.PostAsJsonAsync("/api/auth/logout", new { RefreshToken = raw });
            using var refresh = await client.PostAsJsonAsync("/api/auth/refresh", new { RefreshToken = raw });

            Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
            Assert.Equal("", await first.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
            await using var context = factory.CreateDbContext();
            var userId = await context.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
            var stored = Assert.Single(await context.RefreshTokens.Where(t => t.UserId == userId).ToListAsync());
            Assert.NotNull(stored.RevokedAtUtc);
            Assert.Equal(DateTimeKind.Utc, stored.RevokedAtUtc.Value.Kind);
            Assert.False(stored.TokenHash == raw);
            Assert.True(stored.TokenHash == new RefreshTokenGenerator().Hash(raw));
            Assert.False(JsonSerializer.Serialize(stored).Contains(raw, StringComparison.Ordinal));
        }
        finally { await factory.CleanupAsync(email); }
    }

    [Fact]
    public async Task Logout_AfterRotation_RevokesFamilyWhileAnotherSessionKeepsWorking()
    {
        using var factory = new LogoutFactory();
        using var client = factory.CreateClient();
        var email = $"logout-family-{Guid.NewGuid():N}@example.com";
        try
        {
            await RegisterAsync(client, email);
            var firstFamilyRaw = await LoginAsync(client, email);
            var otherFamilyRaw = await LoginAsync(client, email);
            using var rotated = await client.PostAsJsonAsync("/api/auth/refresh",
                new { RefreshToken = firstFamilyRaw });
            Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
            using var json = JsonDocument.Parse(await rotated.Content.ReadAsStringAsync());
            var childRaw = json.RootElement.GetProperty("refreshToken").GetString()!;

            using var logout = await client.PostAsJsonAsync("/api/auth/logout", new { RefreshToken = childRaw });
            Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
            using var replayChild = await client.PostAsJsonAsync("/api/auth/refresh", new { RefreshToken = childRaw });
            using var replayParent = await client.PostAsJsonAsync("/api/auth/refresh", new { RefreshToken = firstFamilyRaw });
            Assert.Equal(HttpStatusCode.Unauthorized, replayChild.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, replayParent.StatusCode);
            using var other = await client.PostAsJsonAsync("/api/auth/refresh", new { RefreshToken = otherFamilyRaw });
            Assert.Equal(HttpStatusCode.OK, other.StatusCode);
            using var otherJson = JsonDocument.Parse(await other.Content.ReadAsStringAsync());
            var otherReplacementRaw = otherJson.RootElement.GetProperty("refreshToken").GetString()!;
            await using var context = factory.CreateDbContext();
            var userId = await context.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
            var all = await context.RefreshTokens.AsNoTracking().Where(t => t.UserId == userId).ToListAsync();
            var generator = new RefreshTokenGenerator();
            var root = Assert.Single(all, t => t.TokenHash == generator.Hash(firstFamilyRaw));
            var child = Assert.Single(all, t => t.TokenHash == generator.Hash(childRaw));
            var separate = Assert.Single(all, t => t.TokenHash == generator.Hash(otherFamilyRaw));
            Assert.Equal(root.Id, child.ParentTokenId);
            Assert.Equal(child.Id, root.ReplacedByTokenId);
            Assert.Equal(root.FamilyId, child.FamilyId);
            Assert.NotNull(root.RevokedAtUtc);
            Assert.NotNull(child.RevokedAtUtc);
            Assert.NotNull(separate.RevokedAtUtc);
            var otherReplacement = Assert.Single(all, t => t.TokenHash == generator.Hash(otherReplacementRaw));
            Assert.Null(otherReplacement.RevokedAtUtc);
            Assert.Equal(separate.FamilyId, otherReplacement.FamilyId);
            Assert.False(root.FamilyId == separate.FamilyId);
            var serialized = JsonSerializer.Serialize(all);
            Assert.False(serialized.Contains(firstFamilyRaw, StringComparison.Ordinal));
            Assert.False(serialized.Contains(childRaw, StringComparison.Ordinal));
            Assert.False(serialized.Contains(otherFamilyRaw, StringComparison.Ordinal));
            Assert.False(serialized.Contains(otherReplacementRaw, StringComparison.Ordinal));
        }
        finally { await factory.CleanupAsync(email); }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("oversized")]
    public async Task Logout_InvalidStructure_Returns400(string? input)
    {
        using var factory = new LogoutFactory();
        using var client = factory.CreateClient();
        if (input == "oversized") input = new string('x', 513);
        using var response = await client.PostAsJsonAsync("/api/auth/logout", new { RefreshToken = input });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Invalid logout input", json.RootElement.GetProperty("title").GetString());
        Assert.False(json.RootElement.TryGetProperty("detail", out _));
    }

    [Fact]
    public async Task Logout_UnknownToken_Returns204()
    {
        using var factory = new LogoutFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/auth/logout", new { RefreshToken = "unknown-token" });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static async Task RegisterAsync(HttpClient client, string email)
    {
        using var response = await client.PostAsJsonAsync("/api/auth/register", new { Email = email, Password });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task<string> LoginAsync(HttpClient client, string email)
    {
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("refreshToken").GetString()!;
    }

    private sealed class LogoutFactory : WebApplicationFactory<Program>
    {
        private readonly string _connection = new ConfigurationBuilder()
            .AddUserSecrets<LogoutHttpTests>(optional: true).AddEnvironmentVariables().Build()
            .GetConnectionString("FinCoreDatabase")
            ?? throw new InvalidOperationException("Configure ConnectionStrings:FinCoreDatabase for HTTP tests.");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddUserSecrets<LogoutHttpTests>(optional: true).AddEnvironmentVariables());
            builder.UseSetting("ConnectionStrings:FinCoreDatabase", _connection);
        }

        public FinCoreDbContext CreateDbContext() => new(
            new DbContextOptionsBuilder<FinCoreDbContext>().UseNpgsql(_connection).Options);

        public async Task CleanupAsync(string email)
        {
            await using var context = CreateDbContext();
            await using var transaction = await context.Database.BeginTransactionAsync();
            var userIds = await context.Users.Where(u => u.Email == email).Select(u => u.Id).ToArrayAsync();
            await context.RefreshTokens.Where(t => userIds.Contains(t.UserId)).ExecuteUpdateAsync(setters => setters
                .SetProperty(t => t.ParentTokenId, (Guid?)null)
                .SetProperty(t => t.ReplacedByTokenId, (Guid?)null));
            await context.RefreshTokens.Where(t => userIds.Contains(t.UserId)).ExecuteDeleteAsync();
            await context.Accounts.Where(a => userIds.Contains(a.UserId)).ExecuteDeleteAsync();
            await context.Users.Where(u => userIds.Contains(u.Id)).ExecuteDeleteAsync();
            await transaction.CommitAsync();
            Assert.False(await context.RefreshTokens.AnyAsync(t => userIds.Contains(t.UserId)));
        }
    }
}
