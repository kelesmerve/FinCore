namespace FinCore.Application.Features.Users.BootstrapAdmin;

public sealed class AdminBootstrapConflictException : Exception
{
    public AdminBootstrapConflictException() : base("An existing customer cannot be promoted by bootstrap.") { }
}
