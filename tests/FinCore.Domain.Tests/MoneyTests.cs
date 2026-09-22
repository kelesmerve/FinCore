using FinCore.Domain.ValueObjects;
using Xunit;

namespace FinCore.Domain.Tests;

public class MoneyTests
{
    [Fact]
    public void Constructor_WithNegativeAmount_Throws()
    {
        // Arrange
        const decimal amount = -0.01m;

        // Act
        Action act = () => new Money(amount);

        // Assert
        Assert.Throws<ArgumentOutOfRangeException>(act);
    }

    [Fact]
    public void Constructor_WithMoreThanTwoDecimalPlaces_Throws()
    {
        // Arrange
        const decimal amount = 1.001m;

        // Act
        Action act = () => new Money(amount);

        // Assert
        Assert.Throws<ArgumentException>(act);
    }

    [Fact]
    public void Equals_WithSameAmount_ReturnsTrue()
    {
        // Arrange
        var first = new Money(12.30m);
        var second = new Money(12.3m);

        // Act
        var equal = first.Equals(second);

        // Assert
        Assert.True(equal);
        Assert.True(first == second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Add_ReturnsSum_WithoutChangingOperands()
    {
        // Arrange
        var original = new Money(10.25m);
        var addition = new Money(2.50m);

        // Act
        var result = original.Add(addition);

        // Assert
        Assert.Equal(new Money(12.75m), result);
        Assert.Equal(10.25m, original.Amount);
        Assert.Equal(2.50m, addition.Amount);
        Assert.NotSame(original, result);
    }

    [Fact]
    public void Subtract_WithSufficientAmount_ReturnsDifference()
    {
        // Arrange
        var original = new Money(10.25m);
        var deduction = new Money(2.50m);

        // Act
        var result = original.Subtract(deduction);

        // Assert
        Assert.Equal(new Money(7.75m), result);
        Assert.Equal(10.25m, original.Amount);
    }

    [Fact]
    public void Subtract_WithLargerAmount_Throws()
    {
        // Arrange
        var original = new Money(10m);
        var deduction = new Money(10.01m);

        // Act
        Action act = () => original.Subtract(deduction);

        // Assert
        Assert.Throws<InvalidOperationException>(act);
        Assert.Equal(10m, original.Amount);
    }
}
