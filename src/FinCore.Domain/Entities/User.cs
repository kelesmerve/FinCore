namespace FinCore.Domain.Entities;

public sealed class User
{
    public Guid Id { get; private set; }
    public string Email { get; private set; } = null!;
    public string PasswordHash { get; private set; } = null!;
    public UserRole Role { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    private User()
    {
    }

    public static User CreateCustomer(string email, string passwordHash)
        => Create(email, passwordHash, UserRole.Customer);

    public static User CreateAdmin(string email, string passwordHash)
        => Create(email, passwordHash, UserRole.Admin);

    private static User Create(string email, string passwordHash, UserRole role)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        return new User
        {
            Id = Guid.NewGuid(),
            Email = email.Trim().ToLowerInvariant(),
            PasswordHash = passwordHash,
            Role = role,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}
