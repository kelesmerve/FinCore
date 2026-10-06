using FinCore.Domain.Entities;

namespace FinCore.Application.Abstractions.Persistence;

public interface ITransferStore
{
    Task<Account?> FindAccountAsync(Guid accountId, CancellationToken cancellationToken);

    Task SaveTransferAsync(
        Account source, Account destination, LedgerTransaction transaction,
        CancellationToken cancellationToken);
}
