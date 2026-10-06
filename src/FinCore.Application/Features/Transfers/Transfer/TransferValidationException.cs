namespace FinCore.Application.Features.Transfers.Transfer;

public sealed class TransferValidationException(string message) : Exception(message);
