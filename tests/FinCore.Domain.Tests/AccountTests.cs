using FinCore.Domain.Entities;
using FinCore.Domain.ValueObjects;
using Xunit;

namespace FinCore.Domain.Tests;

public class AccountTests
{
    [Fact]
    public void Constructor_WithValidArguments_CreatesActiveAccountWithZeroBalance()
    {
        // Arrange
        var userId = Guid.NewGuid();
        const string accountNumber = "FG001";

        // Act
        var account = new Account(userId, accountNumber);

        // Assert
        Assert.Equal(AccountStatus.Active, account.Status);
        Assert.Equal(Money.Zero, account.Balance);
        Assert.Equal(0m, account.Balance.Amount);
        Assert.Equal("TRY", account.Balance.Currency);
    }

    [Fact]
    public void Constructor_WithEmptyUserId_Throws()
    {
        // Arrange
        var userId = Guid.Empty;

        // Act
        Action act = () => new Account(userId, "FG001");

        // Assert
        Assert.Throws<ArgumentException>(act);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithEmptyOrWhitespaceAccountNumber_Throws(string accountNumber)
    {
        // Arrange
        var userId = Guid.NewGuid();

        // Act
        Action act = () => new Account(userId, accountNumber);

        // Assert
        Assert.Throws<ArgumentException>(act);
    }

    [Fact]
    public void Credit_WithPositiveAmount_IncreasesBalance()
    {
        // Arrange
        var account = new Account(Guid.NewGuid(), "FG001");
        account.Credit(new Money(10m));
        var amount = new Money(2.50m);

        // Act
        account.Credit(amount);

        // Assert
        Assert.Equal(new Money(12.50m), account.Balance);
    }

    [Fact]
    public void Debit_WithSufficientBalance_DecreasesBalance()
    {
        // Arrange
        var account = new Account(Guid.NewGuid(), "FG001");
        account.Credit(new Money(10m));
        var amount = new Money(2.50m);

        // Act
        account.Debit(amount);

        // Assert
        Assert.Equal(new Money(7.50m), account.Balance);
    }

    [Fact]
    public void Credit_WithZeroAmount_ThrowsWithoutChangingBalance()
    {
        // Arrange
        var account = new Account(Guid.NewGuid(), "FG001");
        account.Credit(new Money(10m));

        // Act
        Action act = () => account.Credit(Money.Zero);

        // Assert
        Assert.Throws<ArgumentException>(act);
        Assert.Equal(new Money(10m), account.Balance);
    }

    [Fact]
    public void Debit_WithZeroAmount_ThrowsWithoutChangingBalance()
    {
        // Arrange
        var account = new Account(Guid.NewGuid(), "FG001");
        account.Credit(new Money(10m));

        // Act
        Action act = () => account.Debit(Money.Zero);

        // Assert
        Assert.Throws<ArgumentException>(act);
        Assert.Equal(new Money(10m), account.Balance);
    }

    [Fact]
    public void Debit_WithInsufficientBalance_ThrowsWithoutChangingBalance()
    {
        // Arrange
        var account = new Account(Guid.NewGuid(), "FG001");
        account.Credit(new Money(10m));
        var amount = new Money(10.01m);

        // Act
        Action act = () => account.Debit(amount);

        // Assert
        Assert.Throws<InvalidOperationException>(act);
        Assert.Equal(new Money(10m), account.Balance);
    }

    [Fact]
    public void Credit_WhenAccountIsInactive_ThrowsWithoutChangingBalance()
    {
        // Arrange
        var account = new Account(Guid.NewGuid(), "FG001");
        account.Credit(new Money(10m));
        account.Deactivate();
        var amount = new Money(2m);

        // Act
        Action act = () => account.Credit(amount);

        // Assert
        Assert.Throws<InvalidOperationException>(act);
        Assert.Equal(new Money(10m), account.Balance);
    }

    [Fact]
    public void Debit_WhenAccountIsInactive_ThrowsWithoutChangingBalance()
    {
        // Arrange
        var account = new Account(Guid.NewGuid(), "FG001");
        account.Credit(new Money(10m));
        account.Deactivate();
        var amount = new Money(2m);

        // Act
        Action act = () => account.Debit(amount);

        // Assert
        Assert.Throws<InvalidOperationException>(act);
        Assert.Equal(new Money(10m), account.Balance);
    }
}
