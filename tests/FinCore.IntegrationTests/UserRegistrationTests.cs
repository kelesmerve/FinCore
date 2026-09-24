using FinCore.Application.Abstractions.Persistence;
using FinCore.Application.Abstractions.Security;
using FinCore.Application.Features.Users.Register;
using FinCore.Application.Security;
using FinCore.Domain.Entities;
using FinCore.Domain.ValueObjects;
using FinCore.Infrastructure;
using FinCore.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FinCore.IntegrationTests;

public class UserRegistrationTests
{
    [Fact]
    public async Task Register_PersistsCustomerAndAccount_AndRejectsDuplicateEmail()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets<UserRegistrationTests>(optional: true)
            .AddEnvironmentVariables()
            .Build();
        var connectionString = configuration.GetConnectionString("FinCoreDatabase");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Configure ConnectionStrings:FinCoreDatabase for integration tests.");
        }

        var services = new ServiceCollection();
        services.AddInfrastructure(connectionString);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IUserRegistrationStore>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var generator = scope.ServiceProvider.GetRequiredService<IAccountNumberGenerator>();
        var handler = new RegisterUserHandler(store, hasher, generator);
        var email = $"registration-{Guid.NewGuid():N}@example.com";
        const string password = "Test-only-password!42";

        try
        {
            // Act
            var result = await handler.HandleAsync(new($"  {email.ToUpperInvariant()}  ", password));

            // Assert using a separate DbContext to confirm persisted data.
            await using var verificationScope = provider.CreateAsyncScope();
            var verification = verificationScope.ServiceProvider.GetRequiredService<FinCoreDbContext>();
            var user = await verification.Users.AsNoTracking().SingleAsync(item => item.Id == result.UserId);
            var account = await verification.Accounts.AsNoTracking().SingleAsync(item => item.Id == result.AccountId);
            Assert.Equal(email, user.Email);
            Assert.Equal(email, result.Email);
            Assert.NotEqual(password, user.PasswordHash);
            Assert.True(hasher.Verify(user.PasswordHash, password));
            Assert.Equal(UserRole.Customer, user.Role);
            Assert.True(user.IsActive);
            Assert.Equal(user.Id, account.UserId);
            Assert.Equal(AccountStatus.Active, account.Status);
            Assert.Equal(Money.Zero, account.Balance);
            Assert.Equal(account.AccountNumber, result.AccountNumber);
            Assert.Matches("^FC[0-9A-F]{32}$", account.AccountNumber);
            Assert.NotEqual(account.AccountNumber, generator.Generate());

            await Assert.ThrowsAsync<DuplicateEmailException>(() =>
                handler.HandleAsync(new(email.ToUpperInvariant(), password)));
            Assert.Equal(1, await verification.Users.CountAsync(item => item.Email == email));
            Assert.Equal(1, await verification.Accounts.CountAsync(item => item.UserId == user.Id));
        }
        finally
        {
            await using var cleanupScope = provider.CreateAsyncScope();
            var cleanup = cleanupScope.ServiceProvider.GetRequiredService<FinCoreDbContext>();
            await using var cleanupTransaction = await cleanup.Database.BeginTransactionAsync();
            var userIds = await cleanup.Users.Where(user => user.Email == email)
                .Select(user => user.Id).ToArrayAsync();
            await cleanup.Accounts.Where(account => userIds.Contains(account.UserId)).ExecuteDeleteAsync();
            await cleanup.Users.Where(user => userIds.Contains(user.Id)).ExecuteDeleteAsync();
            await cleanupTransaction.CommitAsync();
        }
    }
}
