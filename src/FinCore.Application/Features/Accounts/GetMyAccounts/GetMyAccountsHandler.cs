using FinCore.Application.Abstractions.Persistence;

namespace FinCore.Application.Features.Accounts.GetMyAccounts;

public sealed class GetMyAccountsHandler(IAccountQueryStore store)
{
    public Task<IReadOnlyCollection<AccountListItem>> HandleAsync(
        GetMyAccountsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.UserId == Guid.Empty)
            throw new ArgumentException("User ID is required.", nameof(query));
        return store.GetByUserIdAsync(query.UserId, cancellationToken);
    }
}
