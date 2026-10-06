namespace FinCore.Application.Features.Transfers.Transfer;

public sealed class IdempotencyConflictException : Exception
{
    public IdempotencyConflictException() : base("Idempotency key was used for a different request.") { }
}
