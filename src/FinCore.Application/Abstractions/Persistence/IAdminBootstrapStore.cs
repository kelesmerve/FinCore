using FinCore.Domain.Entities;

namespace FinCore.Application.Abstractions.Persistence;

public interface IAdminBootstrapStore
{
    Task<UserRole?> FindRoleByEmailAsync(string normalizedEmail, CancellationToken cancellationToken);
    Task<UserRole?> AddIfAbsentAsync(User admin, CancellationToken cancellationToken);
}
