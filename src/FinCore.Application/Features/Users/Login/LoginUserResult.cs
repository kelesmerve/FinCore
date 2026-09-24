namespace FinCore.Application.Features.Users.Login;

public sealed record LoginUserResult(string AccessToken, DateTime ExpiresAtUtc);
