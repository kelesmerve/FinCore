using FinCore.Domain.Entities;

namespace FinCore.Application.Abstractions.Persistence;

public interface IRefreshTokenStore
{
    Task AddAsync(RefreshToken token, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
