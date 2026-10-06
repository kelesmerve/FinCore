using Microsoft.EntityFrameworkCore;
using FinCore.Application.Features.Users.Refresh;
using FinCore.Application.Abstractions.Persistence;
using FinCore.Domain.Entities;

namespace FinCore.Infrastructure.Persistence.Repositories;

public sealed class EfRefreshTokenStore(FinCoreDbContext context) : IRefreshTokenStore
{
    public Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken cancellationToken) =>
        context.RefreshTokens.AsTracking().SingleOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

    public Task<User?> FindUserAsync(Guid userId, CancellationToken cancellationToken) =>
        context.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);

    public async Task AddAsync(RefreshToken token, CancellationToken cancellationToken)
    {
        await context.RefreshTokens.AddAsync(token, cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new InvalidRefreshTokenException();
        }
    }
}
