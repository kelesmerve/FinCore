using FinCore.Domain.Entities;
using Xunit;

namespace FinCore.Domain.Tests;

public sealed class AdminUserTests
{
    [Fact]
    public void CreateAdmin_NormalizesEmailAndCreatesActiveAdmin()
    {
        var before = DateTime.UtcNow;

        var admin = User.CreateAdmin("  ADMIN@Example.COM  ", "test-only-hash");

        Assert.NotEqual(Guid.Empty, admin.Id);
        Assert.Equal("admin@example.com", admin.Email);
        Assert.Equal("test-only-hash", admin.PasswordHash);
        Assert.Equal(UserRole.Admin, admin.Role);
        Assert.True(admin.IsActive);
        Assert.Equal(DateTimeKind.Utc, admin.CreatedAtUtc.Kind);
        Assert.InRange(admin.CreatedAtUtc, before, DateTime.UtcNow);
    }

    [Theory]
    [InlineData(null, "hash")]
    [InlineData("  ", "hash")]
    [InlineData("admin@example.com", null)]
    [InlineData("admin@example.com", "  ")]
    public void CreateAdmin_RejectsMissingEmailOrHash(string? email, string? hash)
    {
        Assert.ThrowsAny<ArgumentException>(() => User.CreateAdmin(email!, hash!));
    }
}
