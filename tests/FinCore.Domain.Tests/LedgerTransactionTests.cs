using FinCore.Domain.Entities;
using FinCore.Domain.ValueObjects;
using Xunit;

namespace FinCore.Domain.Tests;

public class LedgerTransactionTests
{
    [Fact]
    public void CreateTransfer_WithEmptySourceAccountId_Throws()
    {
        // Arrange
        var destinationId = Guid.NewGuid();
        var amount = new Money(10m);

        // Act
        Action act = () => LedgerTransaction.CreateTransfer(Guid.Empty, destinationId, amount);

        // Assert
        Assert.Equal("sourceAccountId", Assert.Throws<ArgumentException>(act).ParamName);
    }

    [Fact]
    public void CreateTransfer_WithEmptyDestinationAccountId_Throws()
    {
        // Arrange
        var sourceId = Guid.NewGuid();
        var amount = new Money(10m);

        // Act
        Action act = () => LedgerTransaction.CreateTransfer(sourceId, Guid.Empty, amount);

        // Assert
        Assert.Equal("destinationAccountId", Assert.Throws<ArgumentException>(act).ParamName);
    }

    [Fact]
    public void CreateTransfer_WithSameSourceAndDestination_Throws()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var amount = new Money(10m);

        // Act
        Action act = () => LedgerTransaction.CreateTransfer(accountId, accountId, amount);

        // Assert
        Assert.Throws<ArgumentException>(act);
    }

    [Fact]
    public void CreateTransfer_WithNullAmount_Throws()
    {
        // Arrange
        var sourceId = Guid.NewGuid();
        var destinationId = Guid.NewGuid();

        // Act
        Action act = () => LedgerTransaction.CreateTransfer(sourceId, destinationId, null!);

        // Assert
        Assert.Equal("amount", Assert.Throws<ArgumentNullException>(act).ParamName);
    }

    [Fact]
    public void CreateTransfer_WithZeroAmount_Throws()
    {
        // Arrange
        var sourceId = Guid.NewGuid();
        var destinationId = Guid.NewGuid();
        var amount = Money.Zero;

        // Act
        Action act = () => LedgerTransaction.CreateTransfer(sourceId, destinationId, amount);

        // Assert
        Assert.Equal("amount", Assert.Throws<ArgumentException>(act).ParamName);
    }

    [Fact]
    public void CreateTransfer_WithValidArguments_CreatesExactlyTwoEntries()
    {
        // Arrange
        var sourceId = Guid.NewGuid();
        var destinationId = Guid.NewGuid();
        var amount = new Money(10.25m);

        // Act
        var transaction = LedgerTransaction.CreateTransfer(sourceId, destinationId, amount);

        // Assert
        Assert.Equal(2, transaction.Entries.Count);
    }

    [Fact]
    public void CreateTransfer_FirstEntryIsDebitForSourceAccount()
    {
        // Arrange
        var sourceId = Guid.NewGuid();
        var destinationId = Guid.NewGuid();
        var amount = new Money(10.25m);

        // Act
        var transaction = LedgerTransaction.CreateTransfer(sourceId, destinationId, amount);

        // Assert
        var first = transaction.Entries.First();
        Assert.Equal(sourceId, first.AccountId);
        Assert.Equal(LedgerEntryType.Debit, first.Type);
    }

    [Fact]
    public void CreateTransfer_SecondEntryIsCreditForDestinationAccount()
    {
        // Arrange
        var sourceId = Guid.NewGuid();
        var destinationId = Guid.NewGuid();
        var amount = new Money(10.25m);

        // Act
        var transaction = LedgerTransaction.CreateTransfer(sourceId, destinationId, amount);

        // Assert
        var second = transaction.Entries.ElementAt(1);
        Assert.Equal(destinationId, second.AccountId);
        Assert.Equal(LedgerEntryType.Credit, second.Type);
    }

    [Fact]
    public void CreateTransfer_BothEntriesReferenceTransactionId()
    {
        // Arrange
        var sourceId = Guid.NewGuid();
        var destinationId = Guid.NewGuid();
        var amount = new Money(10.25m);

        // Act
        var transaction = LedgerTransaction.CreateTransfer(sourceId, destinationId, amount);

        // Assert
        Assert.NotEqual(Guid.Empty, transaction.Id);
        Assert.Equal(2, transaction.Entries.Count);
        Assert.All(transaction.Entries, entry => Assert.Equal(transaction.Id, entry.LedgerTransactionId));
    }

    [Fact]
    public void CreateTransfer_BothEntriesCarryTransferAmount()
    {
        // Arrange
        var sourceId = Guid.NewGuid();
        var destinationId = Guid.NewGuid();
        var amount = new Money(10.25m);

        // Act
        var transaction = LedgerTransaction.CreateTransfer(sourceId, destinationId, amount);

        // Assert
        Assert.Equal(amount, transaction.Amount);
        Assert.Equal(2, transaction.Entries.Count);
        Assert.All(transaction.Entries, entry => Assert.Equal(amount, entry.Amount));
    }

    [Fact]
    public void CreateTransfer_BothEntriesShareTransactionTimestamp()
    {
        // Arrange
        var sourceId = Guid.NewGuid();
        var destinationId = Guid.NewGuid();
        var amount = new Money(10.25m);

        // Act
        var transaction = LedgerTransaction.CreateTransfer(sourceId, destinationId, amount);

        // Assert
        Assert.Equal(DateTimeKind.Utc, transaction.CreatedAtUtc.Kind);
        Assert.Equal(2, transaction.Entries.Count);
        Assert.All(transaction.Entries, entry => Assert.Equal(transaction.CreatedAtUtc, entry.CreatedAtUtc));
    }

    [Fact]
    public void Entries_RejectExternalCollectionModifications()
    {
        // Arrange
        var transaction = LedgerTransaction.CreateTransfer(Guid.NewGuid(), Guid.NewGuid(), new Money(10m));
        var originalEntries = transaction.Entries.ToArray();
        var collection = Assert.IsAssignableFrom<IList<LedgerEntry>>(transaction.Entries);

        // Act
        Action add = () => collection.Add(originalEntries[0]);
        Action remove = () => collection.Remove(originalEntries[0]);
        Action clear = () => collection.Clear();
        Action replace = () => collection[0] = originalEntries[1];

        // Assert
        Assert.True(collection.IsReadOnly);
        Assert.Throws<NotSupportedException>(add);
        Assert.Throws<NotSupportedException>(remove);
        Assert.Throws<NotSupportedException>(clear);
        Assert.Throws<NotSupportedException>(replace);
        Assert.Equal(originalEntries, transaction.Entries.ToArray());
    }
}
