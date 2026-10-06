namespace FinCore.Application.Features.Transfers.Transfer;

public sealed class TransferBusinessRuleException : InvalidOperationException
{
    public TransferBusinessRuleException() : base("The transfer cannot be completed with the current account state.")
    {
    }
}
