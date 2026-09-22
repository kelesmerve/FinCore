using FinCore.Domain.ValueObjects;

namespace FinCore.Domain.Entities;

public sealed class LedgerEntry
{
    public Guid Id { get; }
    public Guid LedgerTransactionId { get; }
    public Guid AccountId { get; }
    public LedgerEntryType Type { get; }
    public Money Amount { get; }
    public DateTime CreatedAtUtc { get; }

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
