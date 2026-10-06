using FinCore.Domain.Entities;

namespace FinCore.Application.Abstractions.Persistence;

public interface IRefreshTokenStore
{
    Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<RefreshToken>> FindUnrevokedFamilyAsync(Guid userId, Guid familyId, CancellationToken cancellationToken);
    Task<User?> FindUserAsync(Guid userId, CancellationToken cancellationToken);
    Task AddAsync(RefreshToken token, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
