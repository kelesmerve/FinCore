using FinCore.Application.Abstractions.Persistence;
using FinCore.Application.Features.Users.BootstrapAdmin;
using FinCore.Application.Features.Users.Register;
using FinCore.Application.Security;
using FinCore.Domain.Entities;
using Xunit;

namespace FinCore.Application.Tests;

public sealed class BootstrapAdminHandlerTests
{
    private const string StrongPassword = "Admin-test-password42!";

    [Fact]
    public async Task ValidInput_CreatesNormalizedActiveAdminWithHashedPassword()
    {
        var store = new FakeStore();
        var hasher = new FakeHasher();
        var handler = new BootstrapAdminHandler(store, hasher);
        using var cancellation = new CancellationTokenSource();

        var result = await handler.HandleAsync("  ADMIN@Example.COM  ", StrongPassword, cancellation.Token);

        Assert.Equal(BootstrapAdminResult.Created, result);
        Assert.Equal("admin@example.com", store.LookupEmail);
        Assert.Equal(cancellation.Token, store.LookupToken);
        Assert.Equal(cancellation.Token, store.SaveToken);
        Assert.Equal(1, hasher.Calls);
        var admin = Assert.IsType<User>(store.SavedUser);
        Assert.Equal("admin@example.com", admin.Email);
        Assert.Equal(UserRole.Admin, admin.Role);
        Assert.True(admin.IsActive);
        Assert.Equal("hashed-for-test", admin.PasswordHash);
        Assert.NotEqual(StrongPassword, admin.PasswordHash);
    }

    [Fact]
    public async Task ExistingAdmin_IsIdempotentAndDoesNotHashOrSave()
    {
        var store = new FakeStore { ExistingRole = UserRole.Admin };
        var hasher = new FakeHasher();
        var handler = new BootstrapAdminHandler(store, hasher);

        var result = await handler.HandleAsync("admin@example.com", StrongPassword);

        Assert.Equal(BootstrapAdminResult.AlreadyExists, result);
        Assert.Null(store.SavedUser);
        Assert.Equal(0, hasher.Calls);
    }

    [Fact]
    public async Task ExistingCustomer_ThrowsTypedConflictWithoutPromotion()
    {
        var store = new FakeStore { ExistingRole = UserRole.Customer };
        var hasher = new FakeHasher();
        var handler = new BootstrapAdminHandler(store, hasher);

        await Assert.ThrowsAsync<AdminBootstrapConflictException>(() =>
            handler.HandleAsync("customer@example.com", StrongPassword));

        Assert.Null(store.SavedUser);
        Assert.Equal(0, hasher.Calls);
    }

    [Theory]
    [InlineData(null, StrongPassword)]
    [InlineData("invalid", StrongPassword)]
    [InlineData("admin@example.com", null)]
    [InlineData("admin@example.com", "Short1!")]
    [InlineData("admin@example.com", "nouppercase42!")]
    [InlineData("admin@example.com", "NOLOWERCASE42!")]
    [InlineData("admin@example.com", "NoDigitsHere!")]
    [InlineData("admin@example.com", "NoSpecial42")]
    public async Task InvalidCredentials_RejectBeforeStoreOrHash(string? email, string? password)
    {
        var store = new FakeStore();
        var hasher = new FakeHasher();
        var handler = new BootstrapAdminHandler(store, hasher);

        await Assert.ThrowsAsync<RegistrationValidationException>(() =>
            handler.HandleAsync(email, password));

        Assert.Null(store.LookupEmail);
        Assert.Null(store.SavedUser);
        Assert.Equal(0, hasher.Calls);
    }

    [Fact]
    public async Task ConcurrentAdminCreation_IsTreatedAsIdempotent()
    {
        var store = new FakeStore { RoleReturnedOnSave = UserRole.Admin };
        var handler = new BootstrapAdminHandler(store, new FakeHasher());

        var result = await handler.HandleAsync("admin@example.com", StrongPassword);

        Assert.Equal(BootstrapAdminResult.AlreadyExists, result);
    }

    private sealed class FakeStore : IAdminBootstrapStore
    {
        public UserRole? ExistingRole { get; init; }
        public UserRole? RoleReturnedOnSave { get; init; }
        public string? LookupEmail { get; private set; }
        public CancellationToken LookupToken { get; private set; }
        public CancellationToken SaveToken { get; private set; }
        public User? SavedUser { get; private set; }

        public Task<UserRole?> FindRoleByEmailAsync(string normalizedEmail, CancellationToken cancellationToken)
        {
            LookupEmail = normalizedEmail;
            LookupToken = cancellationToken;
            return Task.FromResult(ExistingRole);
        }

        public Task<UserRole?> AddIfAbsentAsync(User admin, CancellationToken cancellationToken)
        {
            SavedUser = admin;
            SaveToken = cancellationToken;
            return Task.FromResult(RoleReturnedOnSave);
        }
    }

    private sealed class FakeHasher : IPasswordHasher
    {
        public int Calls { get; private set; }
        public string Hash(string password)
        {
            Calls++;
            Assert.Equal(StrongPassword, password);
            return "hashed-for-test";
        }
        public bool Verify(string passwordHash, string providedPassword) => throw new NotSupportedException();
    }
}
