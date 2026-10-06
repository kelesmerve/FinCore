namespace FinCore.Application.Features.Transfers.Transfer;

public sealed record TransferMoneyCommand(
    Guid RequestingUserId,
    Guid SourceAccountId,
    Guid DestinationAccountId,
    decimal Amount);
