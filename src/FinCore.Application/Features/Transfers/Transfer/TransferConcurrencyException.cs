namespace FinCore.Application.Features.Transfers.Transfer;

public sealed class TransferConcurrencyException : Exception
{
    public TransferConcurrencyException() : base("The transfer could not be completed because an account changed. Please retry.")
    {
    }
}
