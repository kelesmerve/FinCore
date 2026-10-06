using FinCore.Application.Features.Accounts.GetMyAccounts;

namespace FinCore.Application.Abstractions.Persistence;

public interface IAccountQueryStore
{
    Task<IReadOnlyCollection<AccountListItem>> GetByUserIdAsync(
        Guid userId, CancellationToken cancellationToken);
}
