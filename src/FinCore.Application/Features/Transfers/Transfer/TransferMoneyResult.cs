namespace FinCore.Application.Features.Transfers.Transfer;

public sealed record TransferMoneyResult(
    Guid TransactionId,
    Guid SourceAccountId,
    Guid DestinationAccountId,
    decimal Amount,
    string Currency,
    DateTime CreatedAtUtc);
