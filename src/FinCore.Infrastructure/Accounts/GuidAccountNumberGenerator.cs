using FinCore.Application.Abstractions.Security;

namespace FinCore.Infrastructure.Accounts;

public sealed class GuidAccountNumberGenerator : IAccountNumberGenerator
{
    public string Generate() => "FC" + Guid.NewGuid().ToString("N").ToUpperInvariant();
}
