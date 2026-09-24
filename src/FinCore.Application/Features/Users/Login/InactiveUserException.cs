namespace FinCore.Application.Features.Users.Login;

public sealed class InactiveUserException : Exception
{
    public InactiveUserException() : base("User account is inactive.")
    {
    }
}
