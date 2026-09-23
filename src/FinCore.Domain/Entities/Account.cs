using FinCore.Domain.ValueObjects;

namespace FinCore.Domain.Entities;

public sealed class Account
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string AccountNumber { get; private set; } = null!;
    public Money Balance { get; private set; } = null!;
    public AccountStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    private Account()
    {
    }

    public Account(Guid userId, string accountNumber)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("User ID cannot be empty.", nameof(userId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(accountNumber);

        Id = Guid.NewGuid();
        UserId = userId;
        AccountNumber = accountNumber;
        Balance = Money.Zero;
        Status = AccountStatus.Active;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public void Credit(Money amount)
    {
        EnsureMovementAllowed(amount);
        Balance = Balance.Add(amount);
    }

    public void Debit(Money amount)
    {
        EnsureMovementAllowed(amount);

        if (amount.Amount > Balance.Amount)
        {
            throw new InvalidOperationException("Insufficient balance.");
        }

        Balance = Balance.Subtract(amount);
    }

    public void Activate() => Status = AccountStatus.Active;

    public void Deactivate() => Status = AccountStatus.Inactive;

    private void EnsureMovementAllowed(Money amount)
    {
        ArgumentNullException.ThrowIfNull(amount);

        if (amount.Amount == 0m)
        {
            throw new ArgumentException("Movement amount must be greater than zero.", nameof(amount));
        }

        if (Status != AccountStatus.Active)
        {
            throw new InvalidOperationException("Inactive accounts cannot perform money movements.");
        }
    }
}
