namespace FinCore.Domain.ValueObjects;

public sealed record Money
{
    public decimal Amount { get; }
    public string Currency => "TRY";

    public static Money Zero { get; } = new(0m);

    public Money(decimal amount)
    {
        if (amount < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Amount cannot be negative.");
        }

        if (decimal.Round(amount, 2) != amount)
        {
            throw new ArgumentException("Amount cannot have more than two decimal places.", nameof(amount));
        }

        Amount = amount;
    }

    public Money Add(Money amount)
    {
        ArgumentNullException.ThrowIfNull(amount);
        return new Money(Amount + amount.Amount);
    }

    public Money Subtract(Money amount)
    {
        ArgumentNullException.ThrowIfNull(amount);

        if (amount.Amount > Amount)
        {
            throw new InvalidOperationException("Subtraction cannot produce a negative amount.");
        }

        return new Money(Amount - amount.Amount);
    }
}
