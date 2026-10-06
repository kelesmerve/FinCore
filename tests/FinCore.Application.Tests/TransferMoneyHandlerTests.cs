using System.Text.Json;
using FinCore.Application.Abstractions.Persistence;
using FinCore.Application.Features.Transfers.Transfer;
using FinCore.Domain.Entities;
using FinCore.Domain.ValueObjects;
using Xunit;

namespace FinCore.Application.Tests;

public class TransferMoneyHandlerTests
{
    [Fact]
    public async Task ValidTransfer_DebitsCreditsAndPersistsTwoLedgerEntries()
    {
        // Arrange
        var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();

        // Act
        var result = await fixture.Handler.HandleAsync(fixture.Command, cancellation.Token);

        // Assert
        Assert.Equal(new Money(75m), fixture.Source.Balance);
        Assert.Equal(new Money(35m), fixture.Destination.Balance);
        Assert.Equal(1, fixture.Store.SaveCalls);
        Assert.Same(fixture.Source, fixture.Store.SavedSource);
        Assert.Same(fixture.Destination, fixture.Store.SavedDestination);
        var transaction = Assert.IsType<LedgerTransaction>(fixture.Store.SavedTransaction);
        Assert.Equal(2, transaction.Entries.Count);
        var entries = transaction.Entries.ToArray();
        Assert.Equal(fixture.Source.Id, entries[0].AccountId);
        Assert.Equal(LedgerEntryType.Debit, entries[0].Type);
        Assert.Equal(fixture.Destination.Id, entries[1].AccountId);
        Assert.Equal(LedgerEntryType.Credit, entries[1].Type);
        Assert.All(entries, entry =>
        {
            Assert.Equal(transaction.Id, entry.LedgerTransactionId);
            Assert.Equal(new Money(25m), entry.Amount);
            Assert.Equal(transaction.CreatedAtUtc, entry.CreatedAtUtc);
        });
        Assert.Equal(transaction.Id, result.TransactionId);
        Assert.Equal(fixture.Source.Id, result.SourceAccountId);
        Assert.Equal(fixture.Destination.Id, result.DestinationAccountId);
        Assert.Equal(25m, result.Amount);
        Assert.Equal("TRY", result.Currency);
        Assert.Equal(transaction.CreatedAtUtc, result.CreatedAtUtc);
        Assert.Equal(DateTimeKind.Utc, result.CreatedAtUtc.Kind);
        Assert.Equal(new[] { fixture.Source.Id, fixture.Destination.Id }, fixture.Store.Lookups);
        Assert.Equal(3, fixture.Store.CancellationTokens.Count);
        Assert.All(fixture.Store.CancellationTokens, token => Assert.Equal(cancellation.Token, token));
    }

    [Fact]
    public async Task NullCommand_IsRejectedBeforeStoreAccess()
    {
        // Arrange
        var fixture = new Fixture();

        // Act
        var act = () => fixture.Handler.HandleAsync(null!);

        // Assert
        await Assert.ThrowsAsync<ArgumentNullException>(act);
        Assert.Empty(fixture.Store.Lookups);
        Assert.Equal(0, fixture.Store.SaveCalls);
    }

    [Theory]
    [InlineData("user")]
    [InlineData("source")]
    [InlineData("destination")]
    public async Task EmptyIdentifier_IsRejectedBeforeStoreAccess(string field)
    {
        // Arrange
        var fixture = new Fixture();
        var command = field switch
        {
            "user" => fixture.Command with { RequestingUserId = Guid.Empty },
            "source" => fixture.Command with { SourceAccountId = Guid.Empty },
            _ => fixture.Command with { DestinationAccountId = Guid.Empty }
        };

        // Act
        var act = () => fixture.Handler.HandleAsync(command);

        // Assert
        await Assert.ThrowsAsync<TransferValidationException>(act);
        Assert.Empty(fixture.Store.Lookups);
        Assert.Equal(0, fixture.Store.SaveCalls);
    }

    [Fact]
    public async Task SameAccount_IsRejectedBeforeStoreAccess()
    {
        // Arrange
        var fixture = new Fixture();
        var command = fixture.Command with { DestinationAccountId = fixture.Source.Id };

        // Act
        var act = () => fixture.Handler.HandleAsync(command);

        // Assert
        await Assert.ThrowsAsync<TransferValidationException>(act);
        Assert.Empty(fixture.Store.Lookups);
        Assert.Equal(0, fixture.Store.SaveCalls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1.001)]
    public async Task InvalidAmount_IsRejectedBeforeStoreAccess(decimal amount)
    {
        // Arrange
        var fixture = new Fixture();

        // Act
        var act = () => fixture.Handler.HandleAsync(fixture.Command with { Amount = amount });

        // Assert
        await Assert.ThrowsAsync<TransferValidationException>(act);
        Assert.Empty(fixture.Store.Lookups);
        Assert.Equal(0, fixture.Store.SaveCalls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MissingAccount_IsRejectedWithoutSaving(bool missingSource)
    {
        // Arrange
        var fixture = new Fixture();
        var missingId = missingSource ? fixture.Source.Id : fixture.Destination.Id;
        fixture.Store.Accounts.Remove(missingId);

        // Act
        var act = () => fixture.Handler.HandleAsync(fixture.Command);

        // Assert
        await Assert.ThrowsAsync<TransferAccountNotFoundException>(act);
        Assert.Equal(0, fixture.Store.SaveCalls);
        Assert.Null(fixture.Store.SavedTransaction);
        Assert.Equal(new Money(100m), fixture.Source.Balance);
        Assert.Equal(new Money(10m), fixture.Destination.Balance);
    }

    [Fact]
    public async Task ForeignSource_IsRejectedBeforeDestinationLookupOrSave()
    {
        // Arrange
        var fixture = new Fixture();
        var command = fixture.Command with { RequestingUserId = Guid.NewGuid() };

        // Act
        var act = () => fixture.Handler.HandleAsync(command);

        // Assert
        await Assert.ThrowsAsync<TransferAccountAccessDeniedException>(act);
        Assert.Equal(new[] { fixture.Source.Id }, fixture.Store.Lookups);
        Assert.Equal(0, fixture.Store.SaveCalls);
        Assert.Equal(new Money(100m), fixture.Source.Balance);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InactiveAccount_ThrowsTypedBusinessRuleAndDoesNotSave(bool inactiveSource)
    {
        // Arrange
        var fixture = new Fixture();
        if (inactiveSource) fixture.Source.Deactivate();
        else fixture.Destination.Deactivate();

        // Act
        var act = () => fixture.Handler.HandleAsync(fixture.Command);

        // Assert
        await Assert.ThrowsAsync<TransferBusinessRuleException>(act);
        Assert.Equal(0, fixture.Store.SaveCalls);
        Assert.Null(fixture.Store.SavedTransaction);
    }

    [Fact]
    public async Task InsufficientBalance_ThrowsTypedBusinessRuleAndDoesNotSave()
    {
        // Arrange
        var fixture = new Fixture();
        var command = fixture.Command with { Amount = 100.01m };

        // Act
        var act = () => fixture.Handler.HandleAsync(command);

        // Assert
        await Assert.ThrowsAsync<TransferBusinessRuleException>(act);
        Assert.Equal(0, fixture.Store.SaveCalls);
        Assert.Null(fixture.Store.SavedTransaction);
        Assert.Equal(new Money(100m), fixture.Source.Balance);
    }

    [Fact]
    public async Task Result_ContainsOnlyApprovedFields()
    {
        // Arrange
        var fixture = new Fixture();

        // Act
        var result = await fixture.Handler.HandleAsync(fixture.Command);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));

        // Assert
        Assert.Equal(new[] { "Amount", "CreatedAtUtc", "Currency", "DestinationAccountId",
            "SourceAccountId", "TransactionId" },
            json.RootElement.EnumerateObject().Select(property => property.Name).OrderBy(name => name));
        Assert.DoesNotContain("Balance", json.RootElement.GetRawText());
        Assert.DoesNotContain("UserId", json.RootElement.GetRawText());
    }

    private sealed class Fixture
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public Account Source { get; }
        public Account Destination { get; }
        public FakeStore Store { get; } = new();
        public TransferMoneyHandler Handler => new(Store);
        public TransferMoneyCommand Command => new(UserId, Source.Id, Destination.Id, 25m);

        public Fixture()
        {
            Source = new Account(UserId, $"FC{Guid.NewGuid():N}");
            Destination = new Account(Guid.NewGuid(), $"FC{Guid.NewGuid():N}");
            Source.Credit(new Money(100m));
            Destination.Credit(new Money(10m));
            Store.Accounts.Add(Source.Id, Source);
            Store.Accounts.Add(Destination.Id, Destination);
        }
    }

    private sealed class FakeStore : ITransferStore
    {
        public Dictionary<Guid, Account> Accounts { get; } = [];
        public List<Guid> Lookups { get; } = [];
        public List<CancellationToken> CancellationTokens { get; } = [];
        public int SaveCalls { get; private set; }
        public Account? SavedSource { get; private set; }
        public Account? SavedDestination { get; private set; }
        public LedgerTransaction? SavedTransaction { get; private set; }

        public Task<Account?> FindAccountAsync(Guid accountId, CancellationToken cancellationToken)
        {
            Lookups.Add(accountId);
            CancellationTokens.Add(cancellationToken);
            Accounts.TryGetValue(accountId, out var account);
            return Task.FromResult(account);
        }

        public Task SaveTransferAsync(
            Account source, Account destination, LedgerTransaction transaction,
            CancellationToken cancellationToken)
        {
            SaveCalls++;
            CancellationTokens.Add(cancellationToken);
            SavedSource = source;
            SavedDestination = destination;
            SavedTransaction = transaction;
            return Task.CompletedTask;
        }
    }
}
