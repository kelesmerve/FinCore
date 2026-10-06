using FinCore.Application.Features.Transfers.Transfer;
using FinCore.Domain.Entities;
using FinCore.Domain.ValueObjects;
using FinCore.Infrastructure.Persistence;
using FinCore.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FinCore.IntegrationTests;

public sealed class TransferPersistenceTests
{
    private static FinCoreDbContext CreateContext()
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets<TransferPersistenceTests>(optional: true).AddEnvironmentVariables().Build();
        var connection = configuration.GetConnectionString("FinCoreDatabase")
            ?? throw new InvalidOperationException("Configure ConnectionStrings:FinCoreDatabase for integration tests.");
        return new FinCoreDbContext(new DbContextOptionsBuilder<FinCoreDbContext>().UseNpgsql(connection).Options);
    }

    [Fact]
    public async Task IdempotencyRecordFailure_RollsBackTransferBalancesAndLedger()
    {
        var (sourceUser, destinationUser, source, destination) = await SeedAsync();
        try
        {
            await using (var context = CreateContext())
            {
                var transfer = new TransferMoneyHandler(new EfTransferStore(context));
                var idempotency = new EfIdempotentTransferStore(context);
                await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => idempotency.ExecuteAsync(
                    sourceUser.Id, new string('o', 101), Guid.NewGuid().ToString("N"), new string('A', 64),
                    ct => transfer.HandleAsync(new TransferMoneyCommand(
                        sourceUser.Id, source.Id, destination.Id, 20m), ct), default));
            }

            await using var verify = CreateContext();
            Assert.Equal(new Money(100m), (await verify.Accounts.SingleAsync(a => a.Id == source.Id)).Balance);
            Assert.Equal(Money.Zero, (await verify.Accounts.SingleAsync(a => a.Id == destination.Id)).Balance);
            Assert.False(await verify.LedgerTransactions.AnyAsync(t => t.SourceAccountId == source.Id));
            Assert.False(await verify.LedgerEntries.AnyAsync(e => e.AccountId == source.Id || e.AccountId == destination.Id));
            Assert.False(await verify.IdempotencyRecords.AnyAsync(r => r.UserId == sourceUser.Id));
        }
        finally { await CleanupAsync(sourceUser.Id, destinationUser.Id); }
    }

    [Fact]
    public async Task SuccessfulTransfer_PersistsBothBalancesAndBalancedLedger()
    {
        var (sourceUser, destinationUser, source, destination) = await SeedAsync();
        Guid transactionId = Guid.Empty;
        try
        {
            await using (var context = CreateContext())
            {
                var result = await new TransferMoneyHandler(new EfTransferStore(context)).HandleAsync(
                    new TransferMoneyCommand(sourceUser.Id, source.Id, destination.Id, 25.25m));
                transactionId = result.TransactionId;
                context.ChangeTracker.Clear();
            }

            await using var verify = CreateContext();
            var loadedSource = await verify.Accounts.SingleAsync(a => a.Id == source.Id);
            var loadedDestination = await verify.Accounts.SingleAsync(a => a.Id == destination.Id);
            var ledger = await verify.LedgerTransactions.Include(t => t.Entries)
                .SingleAsync(t => t.Id == transactionId);
            Assert.Equal(new Money(74.75m), loadedSource.Balance);
            Assert.Equal(new Money(25.25m), loadedDestination.Balance);
            Assert.Equal(2, ledger.Entries.Count);
            Assert.Contains(ledger.Entries, e => e.AccountId == source.Id && e.Type == LedgerEntryType.Debit);
            Assert.Contains(ledger.Entries, e => e.AccountId == destination.Id && e.Type == LedgerEntryType.Credit);
            Assert.All(ledger.Entries, e =>
            {
                Assert.Equal(transactionId, e.LedgerTransactionId);
                Assert.Equal(new Money(25.25m), e.Amount);
            });
        }
        finally { await CleanupAsync(sourceUser.Id, destinationUser.Id); }
    }

    [Fact]
    public async Task FailedInsert_RollsBackBothBalancesAndNewLedger()
    {
        var (sourceUser, destinationUser, source, destination) = await SeedAsync();
        try
        {
            var duplicate = LedgerTransaction.CreateTransfer(source.Id, destination.Id, new Money(10m));
            await using (var seed = CreateContext())
            {
                seed.LedgerTransactions.Add(duplicate);
                await seed.SaveChangesAsync();
            }

            await using (var context = CreateContext())
            {
                var store = new EfTransferStore(context);
                var trackedSource = (await store.FindAccountAsync(source.Id, default))!;
                var trackedDestination = (await store.FindAccountAsync(destination.Id, default))!;
                trackedSource.Debit(new Money(15m));
                trackedDestination.Credit(new Money(15m));
                await Assert.ThrowsAsync<DbUpdateException>(() =>
                    store.SaveTransferAsync(trackedSource, trackedDestination, duplicate, default));
            }

            await using var verify = CreateContext();
            Assert.Equal(new Money(100m), (await verify.Accounts.SingleAsync(a => a.Id == source.Id)).Balance);
            Assert.Equal(Money.Zero, (await verify.Accounts.SingleAsync(a => a.Id == destination.Id)).Balance);
            Assert.Equal(1, await verify.LedgerTransactions.CountAsync(t => t.SourceAccountId == source.Id));
            Assert.Equal(2, await verify.LedgerEntries.CountAsync(e => e.LedgerTransactionId == duplicate.Id));
        }
        finally { await CleanupAsync(sourceUser.Id, destinationUser.Id); }
    }

    [Fact]
    public async Task StaleAccountVersion_RejectsSecondTransferAndRollsBackItsLedger()
    {
        var (sourceUser, destinationUser, source, destination) = await SeedAsync();
        try
        {
            await using var first = CreateContext();
            await using var second = CreateContext();
            var firstStore = new EfTransferStore(first);
            var secondStore = new EfTransferStore(second);
            var sourceOne = (await firstStore.FindAccountAsync(source.Id, default))!;
            var destinationOne = (await firstStore.FindAccountAsync(destination.Id, default))!;
            var sourceTwo = (await secondStore.FindAccountAsync(source.Id, default))!;
            var destinationTwo = (await secondStore.FindAccountAsync(destination.Id, default))!;
            Assert.True(first.Entry(sourceOne).Property<uint>("xmin").CurrentValue > 0);

            sourceOne.Debit(new Money(20m));
            destinationOne.Credit(new Money(20m));
            sourceTwo.Debit(new Money(30m));
            destinationTwo.Credit(new Money(30m));
            var winner = LedgerTransaction.CreateTransfer(source.Id, destination.Id, new Money(20m));
            var loser = LedgerTransaction.CreateTransfer(source.Id, destination.Id, new Money(30m));

            await firstStore.SaveTransferAsync(sourceOne, destinationOne, winner, default);
            await Assert.ThrowsAsync<TransferConcurrencyException>(() =>
                secondStore.SaveTransferAsync(sourceTwo, destinationTwo, loser, default));

            await using var verify = CreateContext();
            Assert.Equal(new Money(80m), (await verify.Accounts.SingleAsync(a => a.Id == source.Id)).Balance);
            Assert.Equal(new Money(20m), (await verify.Accounts.SingleAsync(a => a.Id == destination.Id)).Balance);
            Assert.Equal(1, await verify.LedgerTransactions.CountAsync(t => t.SourceAccountId == source.Id));
            Assert.False(await verify.LedgerTransactions.AnyAsync(t => t.Id == loser.Id));
            Assert.False(await verify.LedgerEntries.AnyAsync(e => e.LedgerTransactionId == loser.Id));
        }
        finally { await CleanupAsync(sourceUser.Id, destinationUser.Id); }
    }

    private static async Task<(User, User, Account, Account)> SeedAsync()
    {
        var sourceUser = User.CreateCustomer($"transfer-{Guid.NewGuid():N}@example.com", "test-hash");
        var destinationUser = User.CreateCustomer($"transfer-{Guid.NewGuid():N}@example.com", "test-hash");
        var source = new Account(sourceUser.Id, Guid.NewGuid().ToString("N"));
        var destination = new Account(destinationUser.Id, Guid.NewGuid().ToString("N"));
        source.Credit(new Money(100m));
        await using var context = CreateContext();
        context.Users.AddRange(sourceUser, destinationUser);
        context.Accounts.AddRange(source, destination);
        await context.SaveChangesAsync();
        return (sourceUser, destinationUser, source, destination);
    }

    private static async Task CleanupAsync(Guid sourceUserId, Guid destinationUserId)
    {
        await using var context = CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var accountIds = await context.Accounts.Where(a => a.UserId == sourceUserId || a.UserId == destinationUserId)
            .Select(a => a.Id).ToArrayAsync();
        var transactionIds = await context.LedgerTransactions
            .Where(t => accountIds.Contains(t.SourceAccountId) && accountIds.Contains(t.DestinationAccountId))
            .Select(t => t.Id).ToArrayAsync();
        await context.LedgerEntries.Where(e => transactionIds.Contains(e.LedgerTransactionId)).ExecuteDeleteAsync();
        await context.LedgerTransactions.Where(t => transactionIds.Contains(t.Id)).ExecuteDeleteAsync();
        await context.Accounts.Where(a => accountIds.Contains(a.Id)).ExecuteDeleteAsync();
        await context.Users.Where(u => u.Id == sourceUserId || u.Id == destinationUserId).ExecuteDeleteAsync();
        await transaction.CommitAsync();
    }
}
