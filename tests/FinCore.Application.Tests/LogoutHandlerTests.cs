using FinCore.Application.Abstractions.Persistence;
using FinCore.Application.Features.Users.Logout;
using FinCore.Application.Security;
using FinCore.Domain.Entities;
using Xunit;

namespace FinCore.Application.Tests;

public class LogoutHandlerTests
{
    [Fact]
    public async Task ValidToken_RevokesOnlyItsUnrevokedFamilyAndForwardsCancellation()
    {
        // Arrange
        var fake = new FakeDependencies();
        var now = DateTime.UtcNow;
        var root = RefreshToken.CreateInitial(Guid.NewGuid(), "root-hash", now.AddHours(-2), now.AddDays(1));
        var child = root.Rotate("child-hash", now.AddHours(-1), now.AddDays(2));
        var otherFamily = RefreshToken.CreateInitial(root.UserId, "other-hash", now.AddHours(-1), now.AddDays(2));
        var otherUser = RefreshToken.CreateInitial(Guid.NewGuid(), "other-user-hash", now.AddHours(-1), now.AddDays(2));
        fake.Match = child;
        fake.Family = [child];
        using var cancellation = new CancellationTokenSource();

        // Act
        await fake.Handler.HandleAsync(new("raw-token"), cancellation.Token);

        // Assert
        Assert.Equal("raw-token", fake.HashInput);
        Assert.Equal("lookup-hash", fake.LookupHash);
        Assert.Equal(root.UserId, fake.FamilyUserId);
        Assert.Equal(root.FamilyId, fake.FamilyId);
        Assert.Equal(1, fake.SaveCalls);
        Assert.NotNull(child.RevokedAtUtc);
        Assert.Null(otherFamily.RevokedAtUtc);
        Assert.Null(otherUser.RevokedAtUtc);
        Assert.Equal(now.AddHours(-1), root.RevokedAtUtc);
        Assert.Equal(3, fake.Tokens.Count);
        Assert.All(fake.Tokens, token => Assert.Equal(cancellation.Token, token));
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("revoked")]
    [InlineData("expired")]
    public async Task UnknownRevokedOrExpiredToken_IsIdempotent(string state)
    {
        // Arrange
        var fake = new FakeDependencies();
        if (state != "unknown")
        {
            var now = DateTime.UtcNow;
            fake.Match = RefreshToken.CreateInitial(Guid.NewGuid(), "hash", now.AddHours(-2),
                state == "expired" ? now.AddHours(-1) : now.AddDays(1));
            if (state == "revoked") fake.Match.Revoke(now.AddHours(-1));
        }

        // Act
        await fake.Handler.HandleAsync(new("raw-token"));
        await fake.Handler.HandleAsync(new("raw-token"));

        // Assert
        Assert.Equal(0, fake.SaveCalls);
        Assert.Equal(state == "unknown" ? 0 : 2, fake.FamilyCalls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("oversized")]
    public async Task InvalidInput_ThrowsValidationBeforeHashAndLookup(string? input)
    {
        // Arrange
        var fake = new FakeDependencies();
        if (input == "oversized") input = new string('x', 513);

        // Act
        var act = () => fake.Handler.HandleAsync(new(input));

        // Assert
        await Assert.ThrowsAsync<LogoutValidationException>(act);
        Assert.Null(fake.HashInput);
        Assert.Empty(fake.Tokens);
        Assert.Equal(0, fake.SaveCalls);
    }

    private sealed class FakeDependencies : IRefreshTokenStore, IRefreshTokenGenerator
    {
        public RefreshToken? Match { get; set; }
        public IReadOnlyCollection<RefreshToken> Family { get; set; } = [];
        public LogoutHandler Handler => new(this, this);
        public string? HashInput { get; private set; }
        public string? LookupHash { get; private set; }
        public Guid FamilyUserId { get; private set; }
        public Guid FamilyId { get; private set; }
        public int FamilyCalls { get; private set; }
        public int SaveCalls { get; private set; }
        public List<CancellationToken> Tokens { get; } = [];
        public string Hash(string rawToken) { HashInput = rawToken; return "lookup-hash"; }
        public RefreshTokenMaterial Generate() => throw new NotSupportedException();
        public Task<RefreshToken?> FindByHashAsync(string hash, CancellationToken cancellationToken)
        {
            LookupHash = hash;
            Tokens.Add(cancellationToken);
            return Task.FromResult(Match);
        }
        public Task<IReadOnlyCollection<RefreshToken>> FindUnrevokedFamilyAsync(Guid userId, Guid familyId, CancellationToken cancellationToken)
        {
            FamilyUserId = userId;
            FamilyId = familyId;
            FamilyCalls++;
            Tokens.Add(cancellationToken);
            return Task.FromResult(Family);
        }
        public Task<User?> FindUserAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task AddAsync(RefreshToken token, CancellationToken ct) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            SaveCalls++;
            Tokens.Add(cancellationToken);
            return Task.CompletedTask;
        }
    }
}
