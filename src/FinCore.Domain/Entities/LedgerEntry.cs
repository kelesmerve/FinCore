using FinCore.Domain.ValueObjects;

namespace FinCore.Domain.Entities;

public sealed class LedgerEntry
{
    public Guid Id { get; private set; }
    public Guid LedgerTransactionId { get; private set; }
    public Guid AccountId { get; private set; }
    public LedgerEntryType Type { get; private set; }
    public Money Amount { get; private set; } = null!;
    public DateTime CreatedAtUtc { get; private set; }

    private LedgerEntry()
    {
    }

    internal LedgerEntry(
        Guid ledgerTransactionId,
        Guid accountId,
        LedgerEntryType type,
        Money amount,
        DateTime createdAtUtc)
    {
        Id = Guid.NewGuid();
        LedgerTransactionId = ledgerTransactionId;
        AccountId = accountId;
        Type = type;
        Amount = amount;
        CreatedAtUtc = createdAtUtc;
    }
}
