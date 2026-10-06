using FinCore.Application.Abstractions.Persistence;
using FinCore.Application.Features.Accounts.GetMyAccounts;
using Microsoft.EntityFrameworkCore;

namespace FinCore.Infrastructure.Persistence.Repositories;

public sealed class EfAccountQueryStore(FinCoreDbContext context) : IAccountQueryStore
{
    public async Task<IReadOnlyCollection<AccountListItem>> GetByUserIdAsync(
        Guid userId, CancellationToken cancellationToken) =>
        await context.Accounts.AsNoTracking()
            .Where(account => account.UserId == userId)
            .OrderBy(account => account.CreatedAtUtc).ThenBy(account => account.Id)
            .Select(account => new AccountListItem(
                account.Id, account.AccountNumber, account.Balance.Amount,
                "TRY", account.Status.ToString(), account.CreatedAtUtc))
            .ToListAsync(cancellationToken);
}
