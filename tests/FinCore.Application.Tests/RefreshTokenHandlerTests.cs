using FinCore.Application.Abstractions.Persistence;
using FinCore.Application.Features.Users.Refresh;
using FinCore.Application.Security;
using FinCore.Domain.Entities;
using Xunit;

namespace FinCore.Application.Tests;

public class RefreshTokenHandlerTests
{
    [Fact]
    public async Task ValidToken_RotatesLinksFamilyAndForwardsCancellation()
    {
        // Arrange
        var fake = new Dependencies();
        using var cts = new CancellationTokenSource();
        // Act
        var result = await fake.Handler.HandleAsync(new("raw-input"), cts.Token);
        // Assert
        var child = Assert.IsType<RefreshToken>(fake.Added);
        Assert.Equal(fake.Token!.Id, child.ParentTokenId);
        Assert.Equal(child.Id, fake.Token.ReplacedByTokenId);
        Assert.Equal(fake.Token.FamilyId, child.FamilyId);
        Assert.Equal(fake.User!.Id, child.UserId);
        Assert.Equal(child.CreatedAtUtc, fake.Token.RevokedAtUtc);
        Assert.Equal(TimeSpan.FromDays(7), child.ExpiresAtUtc - child.CreatedAtUtc);
        Assert.Equal(DateTimeKind.Utc, child.CreatedAtUtc.Kind);
        Assert.Equal("new-hash", child.TokenHash);
        Assert.Equal("new-raw", result.RefreshToken);
        Assert.Equal("access", result.AccessToken);
        Assert.Equal(child.ExpiresAtUtc, result.RefreshTokenExpiresAtUtc);
        Assert.Equal("raw-input", fake.HashInput);
        Assert.Equal("lookup-hash", fake.LookupHash);
        Assert.Equal(1, fake.SaveCalls);
        Assert.Equal(1, fake.GenerateCalls);
        Assert.Equal(1, fake.JwtCalls);
        Assert.Equal(4, fake.CancellationTokens.Count);
        Assert.All(fake.CancellationTokens, t => Assert.Equal(cts.Token, t));
        Assert.Equal(new[] { "AccessToken", "AccessTokenExpiresAtUtc", "RefreshToken", "RefreshTokenExpiresAtUtc" },
            result.GetType().GetProperties().Select(p => p.Name).OrderBy(n => n));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("expired")]
    [InlineData("revoked")]
    [InlineData("inactive")]
    [InlineData("missing-user")]
    public async Task InvalidState_RejectsWithoutIssuingOrSaving(string state)
    {
        // Arrange
        var fake = new Dependencies();
        if (state == "missing") fake.Token = null;
        if (state == "expired") fake.Token = RefreshToken.CreateInitial(fake.User!.Id, "hash", DateTime.UtcNow.AddDays(-2), DateTime.UtcNow.AddDays(-1));
        if (state == "revoked") fake.Token!.Revoke(DateTime.UtcNow);
        if (state == "inactive") fake.User!.Deactivate();
        if (state == "missing-user") fake.User = null;
        // Act
        var act = () => fake.Handler.HandleAsync(new("raw-input"));
        // Assert
        var exception = await Assert.ThrowsAsync<InvalidRefreshTokenException>(act);
        Assert.Equal("Invalid refresh token.", exception.Message);
        Assert.Equal(0, fake.GenerateCalls);
        Assert.Equal(0, fake.JwtCalls);
        Assert.Equal(0, fake.SaveCalls);
        Assert.Null(fake.Added);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("oversized")]
    public async Task InvalidInput_RejectsBeforeHashOrDatabase(string? input)
    {
        // Arrange
        var fake = new Dependencies();
        if (input == "oversized") input = new string('x', 513);
        // Act
        var act = () => fake.Handler.HandleAsync(new(input));
        // Assert
        await Assert.ThrowsAsync<InvalidRefreshTokenException>(act);
        Assert.Null(fake.HashInput);
        Assert.Empty(fake.CancellationTokens);
        Assert.Equal(0, fake.GenerateCalls);
        Assert.Equal(0, fake.JwtCalls);
    }

    [Fact]
    public async Task ConcurrencyConflict_DoesNotReturnTokens()
    {
        // Arrange
        var fake = new Dependencies { Conflict = true };
        // Act
        var act = () => fake.Handler.HandleAsync(new("raw-input"));
        // Assert
        await Assert.ThrowsAsync<InvalidRefreshTokenException>(act);
        Assert.Equal(1, fake.SaveCalls);
    }

    private sealed class Dependencies : IRefreshTokenStore, IRefreshTokenGenerator, IJwtTokenGenerator
    {
        public User? User { get; set; } = FinCore.Domain.Entities.User.CreateCustomer("test@example.com", "password-hash");
        public RefreshToken? Token { get; set; }
        public RefreshToken? Added { get; private set; }
        public List<CancellationToken> CancellationTokens { get; } = [];
        public string? HashInput { get; private set; }
        public string? LookupHash { get; private set; }
        public int GenerateCalls { get; private set; }
        public int JwtCalls { get; private set; }
        public int SaveCalls { get; private set; }
        public bool Conflict { get; init; }
        public RefreshTokenHandler Handler => new(this, this, this);
        public Dependencies() => Token = RefreshToken.CreateInitial(User!.Id, "lookup-hash", DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddDays(1));
        public string Hash(string rawToken) { HashInput = rawToken; return "lookup-hash"; }
        public RefreshTokenMaterial Generate() { GenerateCalls++; return new("new-raw", "new-hash"); }
        public JwtTokenResult Generate(User user) { JwtCalls++; return new("access", DateTime.UtcNow.AddMinutes(15)); }
        public Task<RefreshToken?> FindByHashAsync(string hash, CancellationToken ct)
        { LookupHash = hash; CancellationTokens.Add(ct); return Task.FromResult(Token); }
        public Task<IReadOnlyCollection<RefreshToken>> FindUnrevokedFamilyAsync(Guid userId, Guid familyId, CancellationToken ct) => throw new NotSupportedException();
        public Task<User?> FindUserAsync(Guid id, CancellationToken ct)
        { CancellationTokens.Add(ct); return Task.FromResult(User); }
        public Task AddAsync(RefreshToken token, CancellationToken ct)
        { Added = token; CancellationTokens.Add(ct); return Task.CompletedTask; }
        public Task SaveChangesAsync(CancellationToken ct)
        {
            SaveCalls++; CancellationTokens.Add(ct);
            if (Conflict) throw new InvalidRefreshTokenException();
            return Task.CompletedTask;
        }
    }
}
