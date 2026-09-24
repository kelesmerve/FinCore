namespace FinCore.Application.Features.Users.ListUsers;

public sealed record ListUsersResult(IReadOnlyCollection<UserListItem> Items, int Page, int PageSize, int TotalCount);
