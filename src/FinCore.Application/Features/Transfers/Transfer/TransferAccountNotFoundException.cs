namespace FinCore.Application.Features.Transfers.Transfer;

public sealed class TransferAccountNotFoundException : Exception
{
    public TransferAccountNotFoundException() : base("Account not found.") { }
}
