using FinCore.Application.Abstractions.Persistence;
using FinCore.Application.Features.Transfers.Transfer;
using FinCore.Domain.Entities;
using Xunit;

namespace FinCore.Application.Tests;

public sealed class IdempotentTransferHandlerTests
{
    [Fact]
    public void Fingerprint_SameFinancialValueUsesSameCanonicalHash()
    {
        var source = Guid.NewGuid();
        var destination = Guid.NewGuid();
        var first = TransferRequestFingerprint.Compute(source, destination, 10m);
        Assert.Equal(first, TransferRequestFingerprint.Compute(source, destination, 10.0m));
        Assert.Equal(first, TransferRequestFingerprint.Compute(source, destination, 10.00m));
        Assert.Equal(64, first.Length);
        Assert.All(first, character => Assert.True(Uri.IsHexDigit(character)));
    }

    [Fact]
    public void Fingerprint_ChangingAnyMeaningfulFieldChangesHash()
    {
        var source = Guid.NewGuid();
        var destination = Guid.NewGuid();
        var original = TransferRequestFingerprint.Compute(source, destination, 10m);
        Assert.NotEqual(original, TransferRequestFingerprint.Compute(Guid.NewGuid(), destination, 10m));
        Assert.NotEqual(original, TransferRequestFingerprint.Compute(source, Guid.NewGuid(), 10m));
        Assert.NotEqual(original, TransferRequestFingerprint.Compute(source, destination, 10.01m));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task InvalidKey_IsRejectedBeforeStore(string? key)
    {
        var store = new FakeIdempotencyStore();
        var handler = new IdempotentTransferHandler(store, new TransferMoneyHandler(new UnusedTransferStore()));
        var command = NewCommand();
        await Assert.ThrowsAsync<TransferValidationException>(() => handler.HandleAsync(command, key));
        Assert.Equal(0, store.Calls);
    }

    [Fact]
    public async Task KeyLongerThan128_IsRejectedBeforeStore()
    {
        var store = new FakeIdempotencyStore();
        var handler = new IdempotentTransferHandler(store, new TransferMoneyHandler(new UnusedTransferStore()));
        await Assert.ThrowsAsync<TransferValidationException>(() => handler.HandleAsync(NewCommand(), new string('k', 129)));
        Assert.Equal(0, store.Calls);
    }

    [Fact]
    public async Task SameKeyWithDifferentRequest_ProducesTypedConflict()
    {
        var store = new FakeIdempotencyStore();
        var handler = new IdempotentTransferHandler(store, new TransferMoneyHandler(new UnusedTransferStore()));
        var command = NewCommand();
        var first = await handler.HandleAsync(command, "same-key");
        Assert.False(first.IsReplay);
        var replay = await handler.HandleAsync(command with { Amount = 10.00m }, "same-key");
        Assert.True(replay.IsReplay);
        await Assert.ThrowsAsync<IdempotencyConflictException>(() =>
            handler.HandleAsync(command with { Amount = 11m }, "same-key"));
        Assert.Equal(3, store.Calls);
    }

    private static TransferMoneyCommand NewCommand() =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 10m);

    private sealed class FakeIdempotencyStore : IIdempotentTransferStore
    {
        private string? _hash;
        private TransferMoneyResult? _result;
        public int Calls { get; private set; }

        public Task<IdempotentTransferResult> ExecuteAsync(
            Guid userId, string operation, string key, string requestHash,
            Func<CancellationToken, Task<TransferMoneyResult>> transfer, CancellationToken cancellationToken)
        {
            Calls++;
            Assert.Equal("money-transfer", operation);
            Assert.Equal("same-key", key);
            if (_hash is not null && _hash != requestHash)
                throw new IdempotencyConflictException();
            if (_result is not null)
                return Task.FromResult(new IdempotentTransferResult(_result, true));
            _hash = requestHash;
            _result = new TransferMoneyResult(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 10m, "TRY", DateTime.UtcNow);
            return Task.FromResult(new IdempotentTransferResult(_result, false));
        }
    }

    private sealed class UnusedTransferStore : ITransferStore
    {
        public Task<Account?> FindAccountAsync(Guid accountId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task SaveTransferAsync(Account source, Account destination, LedgerTransaction transaction,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
