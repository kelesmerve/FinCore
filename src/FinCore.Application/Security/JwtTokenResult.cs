namespace FinCore.Application.Security;

public sealed record JwtTokenResult(string Token, DateTime ExpiresAtUtc);
