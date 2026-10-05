using FinCore.Domain.Entities;
using FinCore.Infrastructure.Persistence;
using FinCore.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FinCore.IntegrationTests;

public class RefreshTokenPersistenceTests
{
    private static FinCoreDbContext CreateContext()
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets<RefreshTokenPersistenceTests>(optional: true).AddEnvironmentVariables().Build();
        var connection = configuration.GetConnectionString("FinCoreDatabase")
            ?? throw new InvalidOperationException("Configure ConnectionStrings:FinCoreDatabase for integration tests.");
        return new(new DbContextOptionsBuilder<FinCoreDbContext>().UseNpgsql(connection).Options);
    }

    [Fact]
    public async Task InitialAndRotation_PersistHashesUtcDatesAndFamilyLinks()
    {
        // Arrange
        await using var context = CreateContext();
        var user = User.CreateCustomer($"refresh-{Guid.NewGuid():N}@example.com", "test-only-hash");
        var now = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        var generator = new RefreshTokenGenerator();
        var hash = generator.Generate().TokenHash;
        var token = RefreshToken.CreateInitial(user.Id, hash, now, now.AddDays(7));
        try
        {
            // Act
            context.Users.Add(user);
            context.RefreshTokens.Add(token);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            var parent = await context.RefreshTokens.SingleAsync(t => t.Id == token.Id);

            // Assert
            Assert.True(parent.TokenHash == hash);
            Assert.Equal(now, parent.CreatedAtUtc);
            Assert.Equal(now.AddDays(7), parent.ExpiresAtUtc);
            Assert.Equal(DateTimeKind.Utc, parent.CreatedAtUtc.Kind);
            Assert.Equal(DateTimeKind.Utc, parent.ExpiresAtUtc.Kind);
            Assert.Equal(parent.Id, parent.FamilyId);
            Assert.Null(parent.ParentTokenId);
            Assert.Null(parent.ReplacedByTokenId);
            Assert.Null(parent.RevokedAtUtc);
            Assert.True(context.Entry(parent).Property<uint>("xmin").CurrentValue > 0);

            // Act
            var child = parent.Rotate(generator.Generate().TokenHash, now.AddHours(1), now.AddDays(8));
            context.RefreshTokens.Add(child);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            var loadedParent = await context.RefreshTokens.SingleAsync(t => t.Id == token.Id);
            var loadedChild = await context.RefreshTokens.SingleAsync(t => t.Id == child.Id);

            // Assert
            Assert.Equal(child.Id, loadedParent.ReplacedByTokenId);
            Assert.Equal(now.AddHours(1), loadedParent.RevokedAtUtc);
            Assert.Equal(DateTimeKind.Utc, loadedParent.RevokedAtUtc!.Value.Kind);
            Assert.Equal(token.Id, loadedChild.ParentTokenId);
            Assert.Equal(token.FamilyId, loadedChild.FamilyId);
            Assert.Equal(user.Id, loadedChild.UserId);
            Assert.True(loadedChild.TokenHash == child.TokenHash);
            Assert.Equal(child.CreatedAtUtc, loadedChild.CreatedAtUtc);
            Assert.Equal(child.ExpiresAtUtc, loadedChild.ExpiresAtUtc);
            Assert.True(loadedChild.IsActive(now.AddHours(1)));
        }
        finally { await CleanupAsync(user.Id); }
    }

    [Fact]
    public async Task ConcurrentRotations_StaleXminRejectsSecondSaveAndRollsBackReplacement()
    {
        // Arrange
        await using var first = CreateContext();
        await using var second = CreateContext();
        var user = User.CreateCustomer($"concurrency-{Guid.NewGuid():N}@example.com", "test-only-hash");
        var now = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        var generator = new RefreshTokenGenerator();
        var token = RefreshToken.CreateInitial(user.Id, generator.Generate().TokenHash, now, now.AddDays(7));
        try
        {
            first.Users.Add(user);
            first.RefreshTokens.Add(token);
            await first.SaveChangesAsync();
            var stale = await second.RefreshTokens.SingleAsync(t => t.Id == token.Id);
            var winner = token.Rotate(generator.Generate().TokenHash, now.AddHours(1), now.AddDays(8));
            var loser = stale.Rotate(generator.Generate().TokenHash, now.AddHours(1), now.AddDays(8));
            first.RefreshTokens.Add(winner);
            second.RefreshTokens.Add(loser);

            // Act
            await first.SaveChangesAsync();

            // Assert
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
            await using var verify = CreateContext();
            var parent = await verify.RefreshTokens.SingleAsync(t => t.Id == token.Id);
            Assert.Equal(winner.Id, parent.ReplacedByTokenId);
            Assert.False(await verify.RefreshTokens.AnyAsync(t => t.Id == loser.Id));
            Assert.Equal(2, await verify.RefreshTokens.CountAsync(t => t.UserId == user.Id));
        }
        finally { await CleanupAsync(user.Id); }
    }

    private static async Task CleanupAsync(Guid userId)
    {
        await using var cleanup = CreateContext();
        await using var transaction = await cleanup.Database.BeginTransactionAsync();
        // Break the bidirectional self references only for this test's records.
        await cleanup.RefreshTokens.Where(t => t.UserId == userId).ExecuteUpdateAsync(setters => setters
            .SetProperty(t => t.ParentTokenId, (Guid?)null)
            .SetProperty(t => t.ReplacedByTokenId, (Guid?)null));
        await cleanup.RefreshTokens.Where(t => t.UserId == userId).ExecuteDeleteAsync();
        await cleanup.Users.Where(u => u.Id == userId).ExecuteDeleteAsync();
        await transaction.CommitAsync();
        Assert.False(await cleanup.RefreshTokens.AnyAsync(t => t.UserId == userId));
        Assert.False(await cleanup.Users.AnyAsync(u => u.Id == userId));
    }
}
