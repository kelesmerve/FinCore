using FinCore.Application.Features.Users.ListUsers;

namespace FinCore.Application.Abstractions.Persistence;

public interface IUserQueryStore
{
    Task<IReadOnlyCollection<UserListItem>> GetPageAsync(int page, int pageSize, CancellationToken cancellationToken);
    Task<int> CountAsync(CancellationToken cancellationToken);
}
