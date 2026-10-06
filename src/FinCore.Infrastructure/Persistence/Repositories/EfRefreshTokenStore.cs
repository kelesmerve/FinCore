using FinCore.Application.Abstractions.Persistence;
using FinCore.Domain.Entities;

namespace FinCore.Infrastructure.Persistence.Repositories;

public sealed class EfRefreshTokenStore(FinCoreDbContext context) : IRefreshTokenStore
{
    public async Task AddAsync(RefreshToken token, CancellationToken cancellationToken)
    {
        await context.RefreshTokens.AddAsync(token, cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        await context.SaveChangesAsync(cancellationToken);
    }
}
