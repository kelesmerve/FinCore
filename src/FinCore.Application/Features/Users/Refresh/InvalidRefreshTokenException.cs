namespace FinCore.Application.Features.Users.Refresh;

public sealed class InvalidRefreshTokenException : Exception
{
    public InvalidRefreshTokenException() : base("Invalid refresh token.") { }
}
