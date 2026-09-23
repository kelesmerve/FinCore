using FinCore.Application.Security;
using Microsoft.AspNetCore.Identity;

namespace FinCore.Infrastructure.Security;

public sealed class AspNetCorePasswordHasher : IPasswordHasher
{
    private readonly PasswordHasher<object> _hasher = new();
    private readonly object _user = new();

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        return _hasher.HashPassword(_user, password);
    }

    public bool Verify(string passwordHash, string providedPassword)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(providedPassword);

        var result = _hasher.VerifyHashedPassword(_user, passwordHash, providedPassword);
        return result is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
    }
}
