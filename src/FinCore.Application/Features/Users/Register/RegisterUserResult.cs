namespace FinCore.Application.Features.Users.Register;

public sealed record RegisterUserResult(Guid UserId, Guid AccountId, string AccountNumber, string Email);
