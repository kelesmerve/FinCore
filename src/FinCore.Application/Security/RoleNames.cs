using FinCore.Domain.Entities;

namespace FinCore.Application.Security;

public static class RoleNames
{
    public const string Customer = nameof(UserRole.Customer);
    public const string Admin = nameof(UserRole.Admin);
}
