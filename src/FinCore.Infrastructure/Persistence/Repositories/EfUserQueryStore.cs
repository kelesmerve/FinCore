using FinCore.Application.Abstractions.Persistence;
using FinCore.Application.Features.Users.ListUsers;
using Microsoft.EntityFrameworkCore;

namespace FinCore.Infrastructure.Persistence.Repositories;

public sealed class EfUserQueryStore(FinCoreDbContext context) : IUserQueryStore
{
    public Task<int> CountAsync(CancellationToken cancellationToken) =>
        context.Users.AsNoTracking().CountAsync(cancellationToken);

    public async Task<IReadOnlyCollection<UserListItem>> GetPageAsync(
        int page, int pageSize, CancellationToken cancellationToken)
    {
        var offset = ((long)page - 1) * pageSize;
        if (offset > int.MaxValue)
            return Array.Empty<UserListItem>();

        return await context.Users.AsNoTracking()
            .OrderBy(user => user.CreatedAtUtc).ThenBy(user => user.Id)
            .Skip((int)offset).Take(pageSize)
            .Select(user => new UserListItem(user.Id, user.Email, user.Role.ToString(),
                user.IsActive, user.CreatedAtUtc))
            .ToListAsync(cancellationToken);
    }
}
