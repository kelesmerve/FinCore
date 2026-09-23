using FinCore.Domain.Entities;
using Xunit;

namespace FinCore.Domain.Tests;

public class UserTests
{
    [Fact]
    public void CreateCustomer_WithValidDetails_CreatesActiveCustomer()
    {
        // Arrange
        const string email = "customer@example.com";
        const string passwordHash = "test-password-hash";
        var before = DateTime.UtcNow;

        // Act
        var user = User.CreateCustomer(email, passwordHash);

        // Assert
        Assert.NotEqual(Guid.Empty, user.Id);
        Assert.Equal(email, user.Email);
        Assert.Equal(passwordHash, user.PasswordHash);
        Assert.Equal(UserRole.Customer, user.Role);
        Assert.True(user.IsActive);
        Assert.Equal(DateTimeKind.Utc, user.CreatedAtUtc.Kind);
        Assert.InRange(user.CreatedAtUtc, before, DateTime.UtcNow);
    }

    [Fact]
    public void CreateCustomer_TrimsAndLowercasesEmail()
    {
        // Arrange
        const string email = "  CUSTOMER@Example.COM  ";

        // Act
        var user = User.CreateCustomer(email, "test-password-hash");

        // Assert
        Assert.Equal("customer@example.com", user.Email);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public void CreateCustomer_WithMissingEmail_Throws(string? email)
    {
        // Arrange
        const string passwordHash = "test-password-hash";

        // Act
        Action act = () => User.CreateCustomer(email!, passwordHash);

        // Assert
        var exception = Assert.ThrowsAny<ArgumentException>(act);
        Assert.Equal("email", exception.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public void CreateCustomer_WithMissingPasswordHash_Throws(string? passwordHash)
    {
        // Arrange
        const string email = "customer@example.com";

        // Act
        Action act = () => User.CreateCustomer(email, passwordHash!);

        // Assert
        var exception = Assert.ThrowsAny<ArgumentException>(act);
        Assert.Equal("passwordHash", exception.ParamName);
    }

    [Fact]
    public void Deactivate_MakesUserInactive()
    {
        // Arrange
        var user = User.CreateCustomer("customer@example.com", "test-password-hash");

        // Act
        user.Deactivate();

        // Assert
        Assert.False(user.IsActive);
    }

    [Fact]
    public void Activate_AfterDeactivation_MakesUserActiveAgain()
    {
        // Arrange
        var user = User.CreateCustomer("customer@example.com", "test-password-hash");
        user.Deactivate();

        // Act
        user.Activate();

        // Assert
        Assert.True(user.IsActive);
    }

    [Fact]
    public void CreateCustomer_WithAdminNamedInputs_DoesNotGrantAdminRole()
    {
        // Arrange
        const string email = "admin@example.com";
        const string passwordHash = "Admin";

        // Act
        var user = User.CreateCustomer(email, passwordHash);

        // Assert
        Assert.Equal(UserRole.Customer, user.Role);
        Assert.NotEqual(UserRole.Admin, user.Role);
    }
}
