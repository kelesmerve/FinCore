using FinCore.Application.Abstractions.Persistence;
using FinCore.Application.Features.Users.Register;
using FinCore.Application.Security;
using FinCore.Domain.Entities;

namespace FinCore.Application.Features.Users.BootstrapAdmin;

public sealed class BootstrapAdminHandler(IAdminBootstrapStore store, IPasswordHasher hasher)
{
    public async Task<BootstrapAdminResult> HandleAsync(
        string? email, string? password, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = RegistrationInputValidator.NormalizeEmail(email);
        RegistrationInputValidator.ValidatePassword(password);

        var existingRole = await store.FindRoleByEmailAsync(normalizedEmail, cancellationToken);
        if (existingRole == UserRole.Admin)
            return BootstrapAdminResult.AlreadyExists;
        if (existingRole == UserRole.Customer)
            throw new AdminBootstrapConflictException();

        var admin = User.CreateAdmin(normalizedEmail, hasher.Hash(password!));
        var concurrentRole = await store.AddIfAbsentAsync(admin, cancellationToken);
        return concurrentRole switch
        {
            null => BootstrapAdminResult.Created,
            UserRole.Admin => BootstrapAdminResult.AlreadyExists,
            _ => throw new AdminBootstrapConflictException()
        };
    }
}
