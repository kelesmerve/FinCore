namespace FinCore.Application.Features.Users.Register;

public sealed class DuplicateEmailException : Exception
{
    public DuplicateEmailException() : base("A user with this email already exists.")
    {
    }
}
