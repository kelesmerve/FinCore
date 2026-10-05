namespace FinCore.Application.Security;

public sealed class RefreshTokenMaterial(string rawToken, string tokenHash)
{
    public string RawToken { get; } = rawToken;
    public string TokenHash { get; } = tokenHash;
}
