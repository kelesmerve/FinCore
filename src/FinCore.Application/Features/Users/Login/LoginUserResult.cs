namespace FinCore.Application.Features.Users.Login;

public sealed record LoginUserResult(
    string AccessToken,
    DateTime AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTime RefreshTokenExpiresAtUtc);
