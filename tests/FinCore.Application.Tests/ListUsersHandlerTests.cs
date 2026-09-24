using System.Text.Json;
using FinCore.Application.Abstractions.Persistence;
using FinCore.Application.Features.Users.ListUsers;
using Xunit;

namespace FinCore.Application.Tests;

public class ListUsersHandlerTests
{
    [Fact]
    public async Task HandleAsync_ValidQuery_ReturnsRequestedPage()
    {
        // Arrange
        var store = new FakeStore();
        var handler = new ListUsersHandler(store);

        // Act
        var result = await handler.HandleAsync(new(2, 10));

        // Assert
        Assert.Equal(2, store.Page);
        Assert.Equal(10, store.PageSize);
        Assert.Equal(2, result.Page);
        Assert.Equal(10, result.PageSize);
        Assert.Equal(store.Items, result.Items);
        Assert.Equal(1, store.PageCalls);
        Assert.Equal(1, store.CountCalls);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(-1, 10)]
    [InlineData(1, 0)]
    [InlineData(1, -1)]
    [InlineData(1, 101)]
    public async Task HandleAsync_InvalidPagination_RejectsBeforeStoreAccess(int page, int pageSize)
    {
        // Arrange
        var store = new FakeStore();
        var handler = new ListUsersHandler(store);

        // Act
        var act = () => handler.HandleAsync(new(page, pageSize));

        // Assert
        await Assert.ThrowsAsync<ListUsersValidationException>(act);
        Assert.Equal(0, store.PageCalls);
        Assert.Equal(0, store.CountCalls);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public async Task HandleAsync_PageSizeBoundary_IsAccepted(int pageSize)
    {
        // Arrange
        var store = new FakeStore();
        var handler = new ListUsersHandler(store);

        // Act
        var result = await handler.HandleAsync(new(1, pageSize));

        // Assert
        Assert.Equal(pageSize, result.PageSize);
        Assert.Equal(pageSize, store.PageSize);
    }

    [Fact]
    public async Task HandleAsync_ReturnsTotalCountFromStore()
    {
        // Arrange
        var store = new FakeStore { TotalCount = 123 };
        var handler = new ListUsersHandler(store);

        // Act
        var result = await handler.HandleAsync(new(1, 10));

        // Assert
        Assert.Equal(123, result.TotalCount);
    }

    [Fact]
    public async Task HandleAsync_ForwardsCancellationTokenToBothStoreMethods()
    {
        // Arrange
        var store = new FakeStore();
        var handler = new ListUsersHandler(store);
        using var cancellation = new CancellationTokenSource();

        // Act
        await handler.HandleAsync(new(1, 10), cancellation.Token);

        // Assert
        Assert.Equal(cancellation.Token, store.CountToken);
        Assert.Equal(cancellation.Token, store.PageToken);
    }

    [Fact]
    public async Task HandleAsync_ResultModelsContainOnlySafeFields()
    {
        // Arrange
        var handler = new ListUsersHandler(new FakeStore());

        // Act
        var result = await handler.HandleAsync(new(1, 10));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));

        // Assert
        Assert.Equal(new[] { "Items", "Page", "PageSize", "TotalCount" },
            json.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n));
        Assert.Equal(new[] { "CreatedAtUtc", "Email", "Id", "IsActive", "Role" },
            json.RootElement.GetProperty("Items")[0].EnumerateObject().Select(p => p.Name).OrderBy(n => n));
        foreach (var type in new[] { typeof(UserListItem), typeof(ListUsersResult) })
        {
            Assert.DoesNotContain(type.GetProperties(), p =>
                p.Name.Contains("Password", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(type.GetFields(), f =>
                f.Name.Contains("Password", StringComparison.OrdinalIgnoreCase));
        }
    }

    private sealed class FakeStore : IUserQueryStore
    {
        public IReadOnlyCollection<UserListItem> Items { get; } =
            [new(Guid.NewGuid(), "customer@example.com", "Customer", true,
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))];
        public int TotalCount { get; init; } = 25;
        public int Page { get; private set; }
        public int PageSize { get; private set; }
        public int PageCalls { get; private set; }
        public int CountCalls { get; private set; }
        public CancellationToken PageToken { get; private set; }
        public CancellationToken CountToken { get; private set; }

        public Task<IReadOnlyCollection<UserListItem>> GetPageAsync(
            int page, int pageSize, CancellationToken cancellationToken)
        {
            Page = page;
            PageSize = pageSize;
            PageToken = cancellationToken;
            PageCalls++;
            return Task.FromResult(Items);
        }

        public Task<int> CountAsync(CancellationToken cancellationToken)
        {
            CountToken = cancellationToken;
            CountCalls++;
            return Task.FromResult(TotalCount);
        }
    }
}
