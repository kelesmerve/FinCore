using FinCore.Domain.Entities;

namespace FinCore.Application.Abstractions.Persistence;

public interface IUserAuthenticationStore
{
    Task<User?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken);
}
