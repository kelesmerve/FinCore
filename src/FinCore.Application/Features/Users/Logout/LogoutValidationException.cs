namespace FinCore.Application.Features.Users.Logout;

public sealed class LogoutValidationException : Exception
{
    public LogoutValidationException() : base("Invalid logout input.") { }
}
