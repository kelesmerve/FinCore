using FinCore.Domain.Entities;
using FinCore.Domain.ValueObjects;
using FinCore.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FinCore.IntegrationTests;

public class PersistenceSmokeTests
{
    [Fact]
    public async Task SaveAndReload_PreservesAccountsMoneyAndLedgerEntries()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets<PersistenceSmokeTests>(optional: true)
            .AddEnvironmentVariables()
            .Build();
        var connectionString = configuration.GetConnectionString("FinCoreDatabase");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Configure ConnectionStrings:FinCoreDatabase in test User Secrets or ConnectionStrings__FinCoreDatabase in the environment.");
        }

        var options = new DbContextOptionsBuilder<FinCoreDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        var source = new Account(Guid.NewGuid(), Guid.NewGuid().ToString("N"));
        var destination = new Account(Guid.NewGuid(), Guid.NewGuid().ToString("N"));
        source.Credit(new Money(100.25m));
        var amount = new Money(25.50m);
        var transaction = LedgerTransaction.CreateTransfer(source.Id, destination.Id, amount);
        var entryIds = transaction.Entries.Select(entry => entry.Id).ToArray();

        await using var context = new FinCoreDbContext(options);
        try
        {
            // Act
            context.Accounts.AddRange(source, destination);
            context.LedgerTransactions.Add(transaction);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var loadedSource = await context.Accounts.SingleAsync(account => account.Id == source.Id);
            var loadedDestination = await context.Accounts.SingleAsync(account => account.Id == destination.Id);
            var loadedTransaction = await context.LedgerTransactions
                .Include(item => item.Entries)
                .SingleAsync(item => item.Id == transaction.Id);

            // Assert
            Assert.NotSame(source, loadedSource);
            Assert.NotSame(destination, loadedDestination);
            Assert.NotSame(transaction, loadedTransaction);
            Assert.Equal(source.UserId, loadedSource.UserId);
            Assert.Equal(source.AccountNumber, loadedSource.AccountNumber);
            Assert.Equal(destination.UserId, loadedDestination.UserId);
            Assert.Equal(destination.AccountNumber, loadedDestination.AccountNumber);
            Assert.Equal(AccountStatus.Active, loadedSource.Status);
            Assert.Equal(AccountStatus.Active, loadedDestination.Status);
            Assert.Equal(new Money(100.25m), loadedSource.Balance);
            Assert.Equal(Money.Zero, loadedDestination.Balance);
            Assert.Equal("TRY", loadedSource.Balance.Currency);
            Assert.Equal("TRY", loadedDestination.Balance.Currency);
            Assert.Equal(source.Id, loadedTransaction.SourceAccountId);
            Assert.Equal(destination.Id, loadedTransaction.DestinationAccountId);
            Assert.Equal(amount, loadedTransaction.Amount);
            Assert.Equal("TRY", loadedTransaction.Amount.Currency);
            Assert.Equal(DateTimeKind.Utc, loadedTransaction.CreatedAtUtc.Kind);
            Assert.Equal(2, loadedTransaction.Entries.Count);

            var debit = Assert.Single(loadedTransaction.Entries, entry => entry.Type == LedgerEntryType.Debit);
            var credit = Assert.Single(loadedTransaction.Entries, entry => entry.Type == LedgerEntryType.Credit);
            Assert.Equal(source.Id, debit.AccountId);
            Assert.Equal(destination.Id, credit.AccountId);
            Assert.All(loadedTransaction.Entries, entry =>
            {
                Assert.Contains(entry.Id, entryIds);
                Assert.Equal(loadedTransaction.Id, entry.LedgerTransactionId);
                Assert.Equal(amount, entry.Amount);
                Assert.Equal("TRY", entry.Amount.Currency);
                Assert.Equal(loadedTransaction.CreatedAtUtc, entry.CreatedAtUtc);
            });
        }
        finally
        {
            await using var cleanup = new FinCoreDbContext(options);
            await using var cleanupTransaction = await cleanup.Database.BeginTransactionAsync();
            await cleanup.LedgerEntries
                .Where(entry => entry.LedgerTransactionId == transaction.Id && entryIds.Contains(entry.Id))
                .ExecuteDeleteAsync();
            await cleanup.LedgerTransactions
                .Where(item => item.Id == transaction.Id)
                .ExecuteDeleteAsync();
            await cleanup.Accounts
                .Where(account => account.Id == source.Id || account.Id == destination.Id)
                .ExecuteDeleteAsync();
            await cleanupTransaction.CommitAsync();
        }
    }
}
