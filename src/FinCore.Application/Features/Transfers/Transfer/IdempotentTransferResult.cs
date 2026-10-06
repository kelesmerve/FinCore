namespace FinCore.Application.Features.Transfers.Transfer;

public sealed record IdempotentTransferResult(TransferMoneyResult Transfer, bool IsReplay);
