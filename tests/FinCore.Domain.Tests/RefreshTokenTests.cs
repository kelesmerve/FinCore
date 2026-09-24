using System.Reflection;
using FinCore.Domain.Entities;
using Xunit;

namespace FinCore.Domain.Tests;

public class RefreshTokenTests
{
    private static readonly DateTime Created = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static RefreshToken Create() => RefreshToken.CreateInitial(Guid.NewGuid(), "test-hash", Created, Created.AddDays(1));

    [Fact]
    public void CreateInitial_ValidInput_CreatesActiveRoot()
    {
        // Arrange
        var userId = Guid.NewGuid();
        // Act
        var token = RefreshToken.CreateInitial(userId, "test-hash", Created, Created.AddDays(1));
        // Assert
        Assert.NotEqual(Guid.Empty, token.Id);
        Assert.Equal(userId, token.UserId);
        Assert.Equal(token.Id, token.FamilyId);
        Assert.Equal("test-hash", token.TokenHash);
        Assert.Equal(Created, token.CreatedAtUtc);
        Assert.Equal(Created.AddDays(1), token.ExpiresAtUtc);
        Assert.Null(token.ParentTokenId);
        Assert.Null(token.ReplacedByTokenId);
        Assert.Null(token.RevokedAtUtc);
        Assert.True(token.IsActive(Created));
    }

    [Fact]
    public void CreateInitial_EmptyUserId_IsRejected()
    {
        // Arrange
        var id = Guid.Empty;
        // Act
        var act = () => RefreshToken.CreateInitial(id, "hash", Created, Created.AddDays(1));
        // Assert
        Assert.Throws<ArgumentException>(act);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void CreateAndRotate_EmptyHash_IsRejectedWithoutMutation(string? hash)
    {
        // Arrange
        var token = Create();
        // Act
        var initial = () => RefreshToken.CreateInitial(Guid.NewGuid(), hash!, Created, Created.AddDays(1));
        var rotate = () => token.Rotate(hash!, Created.AddHours(1), Created.AddDays(2));
        // Assert
        Assert.ThrowsAny<ArgumentException>(initial);
        Assert.ThrowsAny<ArgumentException>(rotate);
        Assert.Null(token.RevokedAtUtc);
        Assert.Null(token.ReplacedByTokenId);
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void AllDateInputs_NonUtc_AreRejected(DateTimeKind kind)
    {
        // Arrange
        var token = Create();
        var invalid = DateTime.SpecifyKind(Created.AddHours(1), kind);
        // Act
        Action[] actions =
        [
            () => RefreshToken.CreateInitial(Guid.NewGuid(), "hash", invalid, Created.AddDays(2)),
            () => RefreshToken.CreateInitial(Guid.NewGuid(), "hash", Created, invalid),
            () => token.IsActive(invalid),
            () => token.IsExpired(invalid),
            () => token.Rotate("new-hash", invalid, Created.AddDays(2)),
            () => token.Rotate("new-hash", Created, invalid),
            () => token.Revoke(invalid)
        ];
        // Assert
        foreach (var action in actions) Assert.Throws<ArgumentException>(action);
        Assert.Null(token.RevokedAtUtc);
        Assert.Null(token.ReplacedByTokenId);
        token.Revoke(Created);
        Assert.Throws<ArgumentException>(() => token.IsActive(invalid));
        Assert.Throws<ArgumentException>(() => token.Revoke(invalid));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void CreateAndRotate_ExpirationNotAfterCreation_IsRejected(int seconds)
    {
        // Arrange
        var token = Create();
        var rotation = Created.AddHours(1);
        // Act
        var initial = () => RefreshToken.CreateInitial(Guid.NewGuid(), "hash", Created, Created.AddSeconds(seconds));
        var rotate = () => token.Rotate("new-hash", rotation, rotation.AddSeconds(seconds));
        // Assert
        Assert.Throws<ArgumentException>(initial);
        Assert.Throws<ArgumentException>(rotate);
        Assert.True(token.IsActive(rotation));
        Assert.Null(token.ReplacedByTokenId);
    }

    [Theory]
    [InlineData(86399, false)]
    [InlineData(86400, true)]
    [InlineData(86401, true)]
    public void Status_ExpirationBoundary_IsRespected(int seconds, bool expired)
    {
        // Arrange
        var token = Create();
        var now = Created.AddSeconds(seconds);
        // Act
        var isExpired = token.IsExpired(now);
        var isActive = token.IsActive(now);
        // Assert
        Assert.Equal(expired, isExpired);
        Assert.Equal(!expired, isActive);
    }

    [Fact]
    public void Rotate_ActiveToken_RevokesParentAndLinksReplacementInSameFamily()
    {
        // Arrange
        var token = Create();
        var rotation = Created.AddHours(1);
        // Act
        var child = token.Rotate("new-hash", rotation, Created.AddDays(2));
        var grandchild = child.Rotate("third-hash", rotation.AddHours(1), Created.AddDays(3));
        // Assert
        Assert.Equal(rotation, token.RevokedAtUtc);
        Assert.False(token.IsActive(rotation));
        Assert.Equal(child.Id, token.ReplacedByTokenId);
        Assert.Equal(token.Id, child.ParentTokenId);
        Assert.NotEqual(token.Id, child.Id);
        Assert.Equal(token.UserId, child.UserId);
        Assert.Equal(token.FamilyId, child.FamilyId);
        Assert.Equal("new-hash", child.TokenHash);
        Assert.Equal(rotation, child.CreatedAtUtc);
        Assert.Equal(Created.AddDays(2), child.ExpiresAtUtc);
        Assert.Equal(token.FamilyId, grandchild.FamilyId);
        Assert.Equal(child.Id, grandchild.ParentTokenId);
        Assert.True(grandchild.IsActive(rotation.AddHours(1)));
        Assert.Null(grandchild.RevokedAtUtc);
        Assert.Null(grandchild.ReplacedByTokenId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Rotate_InactiveToken_IsRejected(bool revoked)
    {
        // Arrange
        var token = Create();
        if (revoked) token.Revoke(Created);
        var now = revoked ? Created.AddHours(1) : Created.AddDays(1);
        // Act
        var act = () => token.Rotate("new-hash", now, now.AddDays(1));
        // Assert
        Assert.Throws<InvalidOperationException>(act);
        Assert.Null(token.ReplacedByTokenId);
        Assert.Equal(revoked ? Created : (DateTime?)null, token.RevokedAtUtc);
    }

    [Fact]
    public void Revoke_IsIdempotentAndDoesNotCreateReplacement()
    {
        // Arrange
        var token = Create();
        var first = Created.AddHours(1);
        // Act
        token.Revoke(first);
        token.Revoke(first.AddHours(1));
        // Assert
        Assert.Equal(first, token.RevokedAtUtc);
        Assert.Null(token.ReplacedByTokenId);
        Assert.False(token.IsActive(first));
    }

    [Fact]
    public void Revoke_AfterRotation_PreservesReplacementAndRevocation()
    {
        // Arrange
        var token = Create();
        var child = token.Rotate("new-hash", Created.AddHours(1), Created.AddDays(2));
        // Act
        token.Revoke(Created.AddHours(2));
        // Assert
        Assert.Equal(Created.AddHours(1), token.RevokedAtUtc);
        Assert.Equal(child.Id, token.ReplacedByTokenId);
        Assert.True(child.IsActive(Created.AddHours(2)));
    }

    [Fact]
    public void RevokeAndRotate_BeforeCreation_AreRejected()
    {
        // Arrange
        var token = Create();
        // Act
        var revoke = () => token.Revoke(Created.AddSeconds(-1));
        var rotate = () => token.Rotate("new-hash", Created.AddSeconds(-1), Created.AddDays(2));
        // Assert
        Assert.Throws<ArgumentException>(revoke);
        Assert.Throws<ArgumentException>(rotate);
        Assert.Null(token.RevokedAtUtc);
        Assert.Null(token.ReplacedByTokenId);
    }

    [Fact]
    public void Entity_ContainsOnlyHashAndPrivateSettersAndConstructor()
    {
        // Arrange
        var type = typeof(RefreshToken);
        // Act
        var properties = type.GetProperties();
        var strings = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(f => f.FieldType == typeof(string)).ToArray();
        // Assert
        Assert.Equal(new[] { "CreatedAtUtc", "ExpiresAtUtc", "FamilyId", "Id", "ParentTokenId",
            "ReplacedByTokenId", "RevokedAtUtc", "TokenHash", "UserId" },
            properties.Select(p => p.Name).OrderBy(n => n));
        Assert.All(properties, p => Assert.True(p.GetSetMethod(true)!.IsPrivate));
        Assert.Equal("TokenHash", Assert.Single(properties, p => p.PropertyType == typeof(string)).Name);
        Assert.Contains("TokenHash", Assert.Single(strings).Name);
        Assert.Empty(type.GetConstructors());
        Assert.True(type.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic,
            null, Type.EmptyTypes, null)!.IsPrivate);
    }
}
