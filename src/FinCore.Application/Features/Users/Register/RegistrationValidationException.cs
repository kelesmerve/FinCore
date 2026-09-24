namespace FinCore.Application.Features.Users.Register;

public sealed class RegistrationValidationException : Exception
{
    public RegistrationValidationException(string message) : base(message)
    {
    }
}
