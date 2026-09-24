using FinCore.Domain.Entities;
using FinCore.Infrastructure.Persistence;
using FinCore.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FinCore.IntegrationTests;

public class UserAuthenticationStoreTests
{
    [Fact]
    public async Task FindByEmailAsync_ReturnsExistingUserWithoutTracking_AndNullForMissingUser()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets<UserAuthenticationStoreTests>(optional: true)
            .AddEnvironmentVariables().Build();
        var connectionString = configuration.GetConnectionString("FinCoreDatabase")
            ?? throw new InvalidOperationException("Configure ConnectionStrings:FinCoreDatabase for integration tests.");
        await using var context = new FinCoreDbContext(
            new DbContextOptionsBuilder<FinCoreDbContext>().UseNpgsql(connectionString).Options);
        var user = User.CreateCustomer($"authentication-{Guid.NewGuid():N}@example.com", "test-only-hash");
        var store = new EfUserAuthenticationStore(context);
        using var cancellation = new CancellationTokenSource();
        try
        {
            context.Users.Add(user);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            // Act
            var found = await store.FindByEmailAsync(user.Email, cancellation.Token);
            var missing = await store.FindByEmailAsync($"missing-{Guid.NewGuid():N}@example.com", cancellation.Token);

            // Assert
            Assert.NotNull(found);
            Assert.Equal(user.Id, found.Id);
            Assert.Equal(user.Email, found.Email);
            Assert.Equal(user.PasswordHash, found.PasswordHash);
            Assert.Null(missing);
            Assert.Empty(context.ChangeTracker.Entries());
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                store.FindByEmailAsync(user.Email, cancellation.Token));
        }
        finally
        {
            await context.Users.Where(item => item.Id == user.Id).ExecuteDeleteAsync();
        }
    }
}
