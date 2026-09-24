using FinCore.Domain.Entities;

namespace FinCore.Application.Abstractions.Persistence;

public interface IUserRegistrationStore
{
    Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken cancellationToken);
    Task AddAsync(User user, Account account, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
