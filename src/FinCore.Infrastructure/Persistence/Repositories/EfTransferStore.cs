using FinCore.Application.Abstractions.Persistence;
using FinCore.Application.Features.Transfers.Transfer;
using FinCore.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinCore.Infrastructure.Persistence.Repositories;

public sealed class EfTransferStore(FinCoreDbContext context) : ITransferStore
{
    public Task<Account?> FindAccountAsync(Guid accountId, CancellationToken cancellationToken) =>
        context.Accounts.AsTracking().SingleOrDefaultAsync(account => account.Id == accountId, cancellationToken);

    public async Task SaveTransferAsync(
        Account source, Account destination, LedgerTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var databaseTransaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await context.LedgerTransactions.AddAsync(transaction, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            await databaseTransaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await databaseTransaction.RollbackAsync(CancellationToken.None);
            throw new TransferConcurrencyException();
        }
        catch
        {
            await databaseTransaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}
