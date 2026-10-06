using System.Text.Json;
using FinCore.Application.Abstractions.Persistence;
using FinCore.Application.Features.Accounts.GetMyAccounts;
using Xunit;

namespace FinCore.Application.Tests;

public sealed class GetMyAccountsHandlerTests
{
    [Fact]
    public async Task HandleAsync_PassesUserIdAndCancellationTokenToStore()
    {
        // Arrange
        var store = new FakeStore();
        var handler = new GetMyAccountsHandler(store);
        var userId = Guid.NewGuid();
        using var cancellation = new CancellationTokenSource();

        // Act
        var result = await handler.HandleAsync(new GetMyAccountsQuery(userId), cancellation.Token);

        // Assert
        Assert.Equal(userId, store.UserId);
        Assert.Equal(cancellation.Token, store.CancellationToken);
        Assert.Same(store.Items, result);
    }

    [Fact]
    public async Task HandleAsync_EmptyStore_ReturnsEmptyList()
    {
        // Arrange
        var handler = new GetMyAccountsHandler(new FakeStore());

        // Act
        var result = await handler.HandleAsync(new GetMyAccountsQuery(Guid.NewGuid()));

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void AccountListItem_ContainsOnlyApprovedFields()
    {
        // Arrange
        var item = new AccountListItem(Guid.NewGuid(), "FC123", 12.50m, "TRY", "Active", DateTime.UtcNow);

        // Act
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(item));

        // Assert
        Assert.Equal(new[] { "AccountNumber", "Balance", "CreatedAtUtc", "Currency", "Id", "Status" },
            json.RootElement.EnumerateObject().Select(property => property.Name).OrderBy(name => name));
        Assert.DoesNotContain(typeof(AccountListItem).GetProperties(), property =>
            property.Name.Contains("User", StringComparison.OrdinalIgnoreCase) ||
            property.Name.Contains("Password", StringComparison.OrdinalIgnoreCase) ||
            property.Name.Contains("Token", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class FakeStore : IAccountQueryStore
    {
        public IReadOnlyCollection<AccountListItem> Items { get; } = Array.Empty<AccountListItem>();
        public Guid UserId { get; private set; }
        public CancellationToken CancellationToken { get; private set; }

        public Task<IReadOnlyCollection<AccountListItem>> GetByUserIdAsync(
            Guid userId, CancellationToken cancellationToken)
        {
            UserId = userId;
            CancellationToken = cancellationToken;
            return Task.FromResult(Items);
        }
    }
}
