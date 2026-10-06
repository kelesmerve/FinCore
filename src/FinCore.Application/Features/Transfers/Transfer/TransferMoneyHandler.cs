using FinCore.Application.Abstractions.Persistence;
using FinCore.Domain.Entities;
using FinCore.Domain.ValueObjects;

namespace FinCore.Application.Features.Transfers.Transfer;

public sealed class TransferMoneyHandler(ITransferStore store)
{
    public async Task<TransferMoneyResult> HandleAsync(
        TransferMoneyCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.RequestingUserId == Guid.Empty ||
            command.SourceAccountId == Guid.Empty ||
            command.DestinationAccountId == Guid.Empty)
            throw new TransferValidationException("User and account IDs are required.");

        if (command.SourceAccountId == command.DestinationAccountId)
            throw new TransferValidationException("Source and destination accounts must be different.");

        if (command.Amount <= 0m || decimal.Round(command.Amount, 2) != command.Amount)
            throw new TransferValidationException("Amount must be positive with at most two decimal places.");

        var amount = new Money(command.Amount);
        var source = await store.FindAccountAsync(command.SourceAccountId, cancellationToken)
            ?? throw new TransferAccountNotFoundException();

        if (source.UserId != command.RequestingUserId)
            throw new TransferAccountAccessDeniedException();

        var destination = await store.FindAccountAsync(command.DestinationAccountId, cancellationToken)
            ?? throw new TransferAccountNotFoundException();

        source.Debit(amount);
        destination.Credit(amount);
        var transaction = LedgerTransaction.CreateTransfer(source.Id, destination.Id, amount);
        await store.SaveTransferAsync(source, destination, transaction, cancellationToken);

        return new TransferMoneyResult(
            transaction.Id, transaction.SourceAccountId, transaction.DestinationAccountId,
            transaction.Amount.Amount, transaction.Amount.Currency, transaction.CreatedAtUtc);
    }
}
