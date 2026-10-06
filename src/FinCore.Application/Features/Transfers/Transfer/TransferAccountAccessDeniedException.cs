namespace FinCore.Application.Features.Transfers.Transfer;

public sealed class TransferAccountAccessDeniedException : Exception
{
    public TransferAccountAccessDeniedException() : base("Source account is not owned by the requesting user.") { }
}
