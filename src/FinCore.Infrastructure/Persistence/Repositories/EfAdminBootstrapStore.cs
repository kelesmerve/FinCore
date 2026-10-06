using FinCore.Application.Abstractions.Persistence;
using FinCore.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FinCore.Infrastructure.Persistence.Repositories;

public sealed class EfAdminBootstrapStore(FinCoreDbContext context) : IAdminBootstrapStore
{
    public Task<UserRole?> FindRoleByEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        context.Users.AsNoTracking().Where(user => user.Email == normalizedEmail)
            .Select(user => (UserRole?)user.Role).SingleOrDefaultAsync(cancellationToken);

    public async Task<UserRole?> AddIfAbsentAsync(User admin, CancellationToken cancellationToken)
    {
        await context.Users.AddAsync(admin, cancellationToken);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return null;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_users_email" })
        {
            context.Entry(admin).State = EntityState.Detached;
            return await FindRoleByEmailAsync(admin.Email, cancellationToken)
                ?? throw new InvalidOperationException("The conflicting user could not be loaded.");
        }
    }
}
