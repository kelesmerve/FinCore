using FinCore.Application.Abstractions.Persistence;

namespace FinCore.Application.Features.Users.ListUsers;

public sealed class ListUsersHandler(IUserQueryStore store)
{
    public async Task<ListUsersResult> HandleAsync(
        ListUsersQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Page < 1)
            throw new ListUsersValidationException("Page must be at least 1.");
        if (query.PageSize is < 1 or > 100)
            throw new ListUsersValidationException("PageSize must be between 1 and 100.");

        var totalCount = await store.CountAsync(cancellationToken);
        var items = await store.GetPageAsync(query.Page, query.PageSize, cancellationToken);
        return new ListUsersResult(items, query.Page, query.PageSize, totalCount);
    }
}
