using FinCore.Application.Features.Transfers.Transfer;

namespace FinCore.Application.Abstractions.Persistence;

public interface IIdempotentTransferStore
{
    Task<IdempotentTransferResult> ExecuteAsync(
        Guid userId, string operation, string key, string requestHash,
        Func<CancellationToken, Task<TransferMoneyResult>> transfer,
        CancellationToken cancellationToken);
}
