using FinCore.Infrastructure.Security;
using Xunit;

namespace FinCore.IntegrationTests;

public class PasswordHasherTests
{
    [Fact]
    public void Hash_ReturnsValueDifferentFromPlaintext()
    {
        // Arrange
        var hasher = new AspNetCorePasswordHasher();
        const string password = "Test-only-password!42";

        // Act
        var hash = hasher.Hash(password);

        // Assert
        Assert.False(string.IsNullOrWhiteSpace(hash));
        Assert.NotEqual(password, hash);
    }

    [Fact]
    public void Hash_SamePasswordTwice_ProducesDifferentHashes()
    {
        // Arrange
        var hasher = new AspNetCorePasswordHasher();
        const string password = "Test-only-password!42";

        // Act
        var first = hasher.Hash(password);
        var second = hasher.Hash(password);

        // Assert
        Assert.NotEqual(first, second);
        Assert.True(hasher.Verify(first, password));
        Assert.True(hasher.Verify(second, password));
    }

    [Fact]
    public void Verify_WithCorrectPassword_ReturnsTrue()
    {
        // Arrange
        var hasher = new AspNetCorePasswordHasher();
        const string password = "Test-only-password!42";
        var hash = hasher.Hash(password);

        // Act
        var result = hasher.Verify(hash, password);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void Verify_WithIncorrectPassword_ReturnsFalse()
    {
        // Arrange
        var hasher = new AspNetCorePasswordHasher();
        var hash = hasher.Hash("Test-only-password!42");

        // Act
        var result = hasher.Verify(hash, "Incorrect-test-password!42");

        // Assert
        Assert.False(result);
    }
}
