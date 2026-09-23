using FinCore.Domain.ValueObjects;

namespace FinCore.Domain.Entities;

public sealed class LedgerTransaction
{
    private readonly List<LedgerEntry> _entries = new();

    public Guid Id { get; private set; }
    public Guid SourceAccountId { get; private set; }
    public Guid DestinationAccountId { get; private set; }
    public Money Amount { get; private set; } = null!;
    public DateTime CreatedAtUtc { get; private set; }
    public IReadOnlyCollection<LedgerEntry> Entries => _entries.AsReadOnly();

    private LedgerTransaction()
    {
    }

    private LedgerTransaction(Guid sourceAccountId, Guid destinationAccountId, Money amount)
    {
        Id = Guid.NewGuid();
        SourceAccountId = sourceAccountId;
        DestinationAccountId = destinationAccountId;
        Amount = amount;
        CreatedAtUtc = DateTime.UtcNow;
        _entries.AddRange(new[]
        {
            new LedgerEntry(Id, SourceAccountId, LedgerEntryType.Debit, Amount, CreatedAtUtc),
            new LedgerEntry(Id, DestinationAccountId, LedgerEntryType.Credit, Amount, CreatedAtUtc)
        });
    }

    public static LedgerTransaction CreateTransfer(
        Guid sourceAccountId,
        Guid destinationAccountId,
        Money amount)
    {
        if (sourceAccountId == Guid.Empty)
        {
            throw new ArgumentException("Source account ID cannot be empty.", nameof(sourceAccountId));
        }

        if (destinationAccountId == Guid.Empty)
        {
            throw new ArgumentException("Destination account ID cannot be empty.", nameof(destinationAccountId));
        }

        if (sourceAccountId == destinationAccountId)
        {
            throw new ArgumentException("Source and destination accounts must be different.", nameof(destinationAccountId));
        }

        ArgumentNullException.ThrowIfNull(amount);

        if (amount.Amount == 0m)
        {
            throw new ArgumentException("Transfer amount must be greater than zero.", nameof(amount));
        }

        return new LedgerTransaction(sourceAccountId, destinationAccountId, amount);
    }
}
