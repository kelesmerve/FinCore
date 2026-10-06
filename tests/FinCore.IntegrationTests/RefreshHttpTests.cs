using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinCore.Application.Abstractions.Persistence;
using FinCore.Domain.Entities;
using FinCore.Infrastructure.Persistence;
using FinCore.Infrastructure.Persistence.Repositories;
using FinCore.Infrastructure.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FinCore.IntegrationTests;

public class RefreshHttpTests
{
    [Fact]
    public async Task Refresh_RotatesHashesAndLinks_RejectsReplay_AndAcceptsReplacement()
    {
        using var factory = new Factory();
        using var client = factory.CreateClient();
        var email = $"refresh-http-{Guid.NewGuid():N}@example.com";
        try
        {
            var original = await LoginAsync(client, email);
            using var response = await client.PostAsJsonAsync("/api/auth/refresh", new { RefreshToken = original });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            using var json = JsonDocument.Parse(body);
            Assert.Equal(new[] { "accessToken", "accessTokenExpiresAtUtc", "refreshToken", "refreshTokenExpiresAtUtc" },
                json.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n));
            var replacement = json.RootElement.GetProperty("refreshToken").GetString()!;
            Assert.False(string.IsNullOrWhiteSpace(replacement));
            Assert.False(original == replacement);
            Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("accessToken").GetString()));
            await using var context = factory.CreateDbContext();
            var user = await context.Users.SingleAsync(u => u.Email == email);
            var tokens = await context.RefreshTokens.AsNoTracking().Where(t => t.UserId == user.Id).ToListAsync();
            Assert.Equal(2, tokens.Count);
            var generator = new RefreshTokenGenerator();
            var parent = Assert.Single(tokens, t => t.TokenHash == generator.Hash(original));
            var child = Assert.Single(tokens, t => t.TokenHash == generator.Hash(replacement));
            Assert.Equal(parent.Id, child.ParentTokenId);
            Assert.Equal(child.Id, parent.ReplacedByTokenId);
            Assert.Equal(parent.FamilyId, child.FamilyId);
            Assert.Equal(child.CreatedAtUtc, parent.RevokedAtUtc);
            Assert.Equal(TimeSpan.FromDays(7), child.ExpiresAtUtc - child.CreatedAtUtc);
            var expires = json.RootElement.GetProperty("refreshTokenExpiresAtUtc").GetDateTime();
            Assert.Equal(DateTimeKind.Utc, expires.Kind);
            Assert.True((expires - child.ExpiresAtUtc).Duration() <= TimeSpan.FromMicroseconds(1));
            var databaseJson = JsonSerializer.Serialize(tokens);
            Assert.False(databaseJson.Contains(original, StringComparison.Ordinal));
            Assert.False(databaseJson.Contains(replacement, StringComparison.Ordinal));
            Assert.False(body.Contains(user.PasswordHash, StringComparison.Ordinal));
            Assert.All(tokens, t => Assert.False(body.Contains(t.TokenHash, StringComparison.Ordinal)));
            using var replay = await client.PostAsJsonAsync("/api/auth/refresh", new { RefreshToken = original });
            await AssertInvalidAsync(replay);
            using var next = await client.PostAsJsonAsync("/api/auth/refresh", new { RefreshToken = replacement });
            Assert.Equal(HttpStatusCode.OK, next.StatusCode);
            Assert.Equal(3, await context.RefreshTokens.CountAsync(t => t.UserId == user.Id));
        }
        finally { await factory.CleanupAsync(email); }
    }

    [Fact]
    public async Task ConcurrentRefresh_OnlyOneSucceeds_AndLosingInsertIsRolledBack()
    {
        using var factory = new Factory(synchronize: true);
        using var client = factory.CreateClient();
        var email = $"refresh-race-{Guid.NewGuid():N}@example.com";
        try
        {
            var raw = await LoginAsync(client, email);
            var responses = await Task.WhenAll(
                client.PostAsJsonAsync("/api/auth/refresh", new { RefreshToken = raw }),
                client.PostAsJsonAsync("/api/auth/refresh", new { RefreshToken = raw }));
            try
            {
                Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
                await AssertInvalidAsync(Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Unauthorized));
                await using var context = factory.CreateDbContext();
                var id = await context.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
                Assert.Equal(2, await context.RefreshTokens.CountAsync(t => t.UserId == id));
                Assert.Equal(1, await context.RefreshTokens.CountAsync(t => t.UserId == id && t.RevokedAtUtc == null));
            }
            finally { foreach (var response in responses) response.Dispose(); }
        }
        finally { await factory.CleanupAsync(email); }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("unknown")]
    [InlineData("oversized")]
    public async Task InvalidInput_ReturnsGeneric401(string? raw)
    {
        using var factory = new Factory();
        using var client = factory.CreateClient();
        if (raw == "oversized") raw = new string('x', 513);
        using var response = await client.PostAsJsonAsync("/api/auth/refresh", new { RefreshToken = raw });
        await AssertInvalidAsync(response);
    }

    private static async Task<string> LoginAsync(HttpClient client, string email)
    {
        const string password = "Refresh-test-password42!";
        using var register = await client.PostAsJsonAsync("/api/auth/register", new { Email = email, Password = password });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        using var login = await client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var json = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("refreshToken").GetString()!;
    }

    private static async Task AssertInvalidAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Invalid refresh token", json.RootElement.GetProperty("title").GetString());
        Assert.False(json.RootElement.TryGetProperty("detail", out _));
        Assert.False(json.RootElement.TryGetProperty("tokenHash", out _));
        Assert.False(json.RootElement.TryGetProperty("passwordHash", out _));
    }

    private sealed class Factory(bool synchronize = false) : WebApplicationFactory<Program>
    {
        private readonly string _connection = new ConfigurationBuilder()
            .AddUserSecrets<RefreshHttpTests>(optional: true).AddEnvironmentVariables().Build()
            .GetConnectionString("FinCoreDatabase")
            ?? throw new InvalidOperationException("Configure ConnectionStrings:FinCoreDatabase for HTTP tests.");
        private readonly ReadGate _gate = new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddUserSecrets<RefreshHttpTests>(optional: true).AddEnvironmentVariables());
            builder.UseSetting("ConnectionStrings:FinCoreDatabase", _connection);
            if (synchronize)
                builder.ConfigureServices(services => services.AddScoped<IRefreshTokenStore>(provider =>
                    new SynchronizedStore(new EfRefreshTokenStore(provider.GetRequiredService<FinCoreDbContext>()), _gate)));
        }
        public FinCoreDbContext CreateDbContext() => new(
            new DbContextOptionsBuilder<FinCoreDbContext>().UseNpgsql(_connection).Options);
        public async Task CleanupAsync(string email)
        {
            await using var context = CreateDbContext();
            await using var transaction = await context.Database.BeginTransactionAsync();
            var ids = await context.Users.Where(u => u.Email == email).Select(u => u.Id).ToArrayAsync();
            await context.RefreshTokens.Where(t => ids.Contains(t.UserId)).ExecuteUpdateAsync(s => s
                .SetProperty(t => t.ParentTokenId, (Guid?)null).SetProperty(t => t.ReplacedByTokenId, (Guid?)null));
            await context.RefreshTokens.Where(t => ids.Contains(t.UserId)).ExecuteDeleteAsync();
            await context.Accounts.Where(a => ids.Contains(a.UserId)).ExecuteDeleteAsync();
            await context.Users.Where(u => ids.Contains(u.Id)).ExecuteDeleteAsync();
            await transaction.CommitAsync();
            Assert.False(await context.RefreshTokens.AnyAsync(t => ids.Contains(t.UserId)));
        }
    }

    private sealed class ReadGate
    {
        private int _readers;
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task WaitAsync(CancellationToken ct)
        {
            if (Interlocked.Increment(ref _readers) == 2) _ready.TrySetResult();
            await _ready.Task.WaitAsync(TimeSpan.FromSeconds(20), ct);
        }
    }

    private sealed class SynchronizedStore(EfRefreshTokenStore inner, ReadGate gate) : IRefreshTokenStore
    {
        public async Task<RefreshToken?> FindByHashAsync(string hash, CancellationToken ct)
        {
            var token = await inner.FindByHashAsync(hash, ct);
            await gate.WaitAsync(ct);
            return token;
        }
        public Task<User?> FindUserAsync(Guid id, CancellationToken ct) => inner.FindUserAsync(id, ct);
        public Task AddAsync(RefreshToken token, CancellationToken ct) => inner.AddAsync(token, ct);
        public Task SaveChangesAsync(CancellationToken ct) => inner.SaveChangesAsync(ct);
    }
}
