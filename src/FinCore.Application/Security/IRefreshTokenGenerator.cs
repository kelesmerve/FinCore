namespace FinCore.Application.Security;

public interface IRefreshTokenGenerator
{
    RefreshTokenMaterial Generate();
    string Hash(string rawToken);
}
