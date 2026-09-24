namespace FinCore.Application.Features.Users.ListUsers;

public sealed record UserListItem(Guid Id, string Email, string Role, bool IsActive, DateTime CreatedAtUtc);
