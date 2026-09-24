using FinCore.Application.Abstractions.Persistence;
using FinCore.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinCore.Infrastructure.Persistence.Repositories;

public sealed class EfUserAuthenticationStore(FinCoreDbContext context) : IUserAuthenticationStore
{
    public Task<User?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        context.Users.AsNoTracking()
            .SingleOrDefaultAsync(user => user.Email == normalizedEmail, cancellationToken);
}
