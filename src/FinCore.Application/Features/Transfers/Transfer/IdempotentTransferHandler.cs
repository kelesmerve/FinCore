using FinCore.Application.Abstractions.Persistence;

namespace FinCore.Application.Features.Transfers.Transfer;

public sealed class IdempotentTransferHandler(
    IIdempotentTransferStore idempotencyStore, TransferMoneyHandler transferHandler)
{
    public Task<IdempotentTransferResult> HandleAsync(
        TransferMoneyCommand command, string? key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(key) || key.Length > 128)
            throw new TransferValidationException("A valid Idempotency-Key is required.");

        var requestHash = TransferRequestFingerprint.Compute(
            command.SourceAccountId, command.DestinationAccountId, command.Amount);
        return idempotencyStore.ExecuteAsync(command.RequestingUserId, "money-transfer", key, requestHash,
            ct => transferHandler.HandleAsync(command, ct), cancellationToken);
    }
}
