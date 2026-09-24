namespace FinCore.Domain.Entities;

public sealed class RefreshToken
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public Guid FamilyId { get; private set; }
    public Guid? ParentTokenId { get; private set; }
    public Guid? ReplacedByTokenId { get; private set; }
    public string TokenHash { get; private set; } = null!;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime? RevokedAtUtc { get; private set; }

    private RefreshToken() { }

    public static RefreshToken CreateInitial(
        Guid userId, string tokenHash, DateTime createdAtUtc, DateTime expiresAtUtc)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("UserId cannot be empty.", nameof(userId));
        ValidateCreation(tokenHash, createdAtUtc, expiresAtUtc);
        var id = Guid.NewGuid();
        return new RefreshToken
        {
            Id = id, UserId = userId, FamilyId = id, TokenHash = tokenHash,
            CreatedAtUtc = createdAtUtc, ExpiresAtUtc = expiresAtUtc
        };
    }

    public bool IsExpired(DateTime utcNow)
    {
        RequireUtc(utcNow, nameof(utcNow));
        return utcNow >= ExpiresAtUtc;
    }

    public bool IsActive(DateTime utcNow)
    {
        RequireUtc(utcNow, nameof(utcNow));
        return RevokedAtUtc is null && !IsExpired(utcNow);
    }

    public RefreshToken Rotate(string newTokenHash, DateTime rotatedAtUtc, DateTime newExpiresAtUtc)
    {
        ValidateCreation(newTokenHash, rotatedAtUtc, newExpiresAtUtc);
        if (rotatedAtUtc < CreatedAtUtc)
            throw new ArgumentException("Rotation cannot precede creation.", nameof(rotatedAtUtc));
        if (!IsActive(rotatedAtUtc))
            throw new InvalidOperationException("Only an active refresh token can be rotated.");

        var replacement = new RefreshToken
        {
            Id = Guid.NewGuid(), UserId = UserId, FamilyId = FamilyId, ParentTokenId = Id,
            TokenHash = newTokenHash, CreatedAtUtc = rotatedAtUtc, ExpiresAtUtc = newExpiresAtUtc
        };
        RevokedAtUtc = rotatedAtUtc;
        ReplacedByTokenId = replacement.Id;
        return replacement;
    }

    public void Revoke(DateTime revokedAtUtc)
    {
        RequireUtc(revokedAtUtc, nameof(revokedAtUtc));
        if (revokedAtUtc < CreatedAtUtc)
            throw new ArgumentException("Revocation cannot precede creation.", nameof(revokedAtUtc));
        if (RevokedAtUtc is not null)
            return;
        RevokedAtUtc = revokedAtUtc;
    }

    private static void ValidateCreation(string tokenHash, DateTime createdAtUtc, DateTime expiresAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        RequireUtc(createdAtUtc, nameof(createdAtUtc));
        RequireUtc(expiresAtUtc, nameof(expiresAtUtc));
        if (expiresAtUtc <= createdAtUtc)
            throw new ArgumentException("Expiration must follow creation.", nameof(expiresAtUtc));
    }

    private static void RequireUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Date must be UTC.", parameterName);
    }
}
