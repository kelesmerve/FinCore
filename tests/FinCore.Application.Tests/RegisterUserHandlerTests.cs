using System.Text.Json;
using FinCore.Application.Abstractions.Persistence;
using FinCore.Application.Abstractions.Security;
using FinCore.Application.Features.Users.Register;
using FinCore.Application.Security;
using FinCore.Domain.Entities;
using FinCore.Domain.ValueObjects;
using Xunit;

namespace FinCore.Application.Tests;

public class RegisterUserHandlerTests
{
    private const string ValidPassword = "Valid-password42!";

    [Fact]
    public async Task HandleAsync_WithValidCommand_CreatesRelatedUserAndAccount()
    {
        // Arrange
        var store = new FakeStore();
        var generator = new FakeAccountNumberGenerator();
        var handler = new RegisterUserHandler(store, new FakeHasher(), generator);

        // Act
        var result = await handler.HandleAsync(new("customer@example.com", ValidPassword));

        // Assert
        var user = Assert.IsType<User>(store.User);
        var account = Assert.IsType<Account>(store.Account);
        Assert.NotEqual(Guid.Empty, user.Id);
        Assert.Equal(UserRole.Customer, user.Role);
        Assert.True(user.IsActive);
        Assert.Equal(user.Id, account.UserId);
        Assert.Equal(AccountStatus.Active, account.Status);
        Assert.Equal(Money.Zero, account.Balance);
        Assert.Equal(generator.Number, account.AccountNumber);
        Assert.Equal(1, generator.Calls);
        Assert.Equal(1, store.AddCalls);
        Assert.Equal(user.Id, result.UserId);
        Assert.Equal(account.Id, result.AccountId);
        Assert.Equal(account.AccountNumber, result.AccountNumber);
        Assert.Equal(user.Email, result.Email);
    }

    [Fact]
    public async Task HandleAsync_NormalizesEmailBeforeLookupAndCreation()
    {
        // Arrange
        var store = new FakeStore();
        var handler = new RegisterUserHandler(store, new FakeHasher(), new FakeAccountNumberGenerator());

        // Act
        var result = await handler.HandleAsync(new("  CUSTOMER@Example.COM  ", ValidPassword));

        // Assert
        Assert.Equal("customer@example.com", store.LookupEmail);
        Assert.Equal("customer@example.com", store.User!.Email);
        Assert.Equal("customer@example.com", result.Email);
    }

    [Fact]
    public async Task HandleAsync_WithDuplicateEmail_RejectsWithoutHashingOrSaving()
    {
        // Arrange
        var store = new FakeStore { EmailExists = true };
        var hasher = new FakeHasher();
        var generator = new FakeAccountNumberGenerator();
        var handler = new RegisterUserHandler(store, hasher, generator);

        // Act
        var act = () => handler.HandleAsync(new("customer@example.com", ValidPassword));

        // Assert
        await Assert.ThrowsAsync<DuplicateEmailException>(act);
        Assert.Equal(0, store.AddCalls);
        Assert.Equal(0, store.SaveCalls);
        Assert.Equal(0, hasher.Calls);
        Assert.Equal(0, generator.Calls);
        Assert.Null(store.User);
        Assert.Null(store.Account);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-email")]
    [InlineData("customer@@example.com")]
    [InlineData("Customer <customer@example.com>")]
    public async Task HandleAsync_WithInvalidEmail_RejectsBeforeHashing(string? email)
    {
        // Arrange
        var store = new FakeStore();
        var hasher = new FakeHasher();
        var handler = new RegisterUserHandler(store, hasher, new FakeAccountNumberGenerator());

        // Act
        var act = () => handler.HandleAsync(new(email!, ValidPassword));

        // Assert
        await Assert.ThrowsAsync<RegistrationValidationException>(act);
        Assert.Equal(0, hasher.Calls);
        Assert.Equal(0, store.LookupCalls);
        Assert.Equal(0, store.AddCalls);
        Assert.Equal(0, store.SaveCalls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Aa1!aaa")]
    [InlineData("lowercase42!")]
    [InlineData("UPPERCASE42!")]
    [InlineData("NoDigitsHere!")]
    [InlineData("NoSpecial42")]
    [InlineData("NoSpecial42 ")]
    public async Task HandleAsync_WithWeakPassword_RejectsBeforeHashing(string? password)
    {
        // Arrange
        var store = new FakeStore();
        var hasher = new FakeHasher();
        var handler = new RegisterUserHandler(store, hasher, new FakeAccountNumberGenerator());

        // Act
        var act = () => handler.HandleAsync(new("customer@example.com", password!));

        // Assert
        await Assert.ThrowsAsync<RegistrationValidationException>(act);
        Assert.Equal(0, hasher.Calls);
        Assert.Equal(0, store.LookupCalls);
        Assert.Equal(0, store.AddCalls);
        Assert.Equal(0, store.SaveCalls);
    }

    [Fact]
    public async Task HandleAsync_WithPasswordOver128Characters_RejectsBeforeHashing()
    {
        // Arrange
        var store = new FakeStore();
        var hasher = new FakeHasher();
        var handler = new RegisterUserHandler(store, hasher, new FakeAccountNumberGenerator());
        var password = "Aa1!" + new string('a', 125);

        // Act
        var act = () => handler.HandleAsync(new("customer@example.com", password));

        // Assert
        await Assert.ThrowsAsync<RegistrationValidationException>(act);
        Assert.Equal(0, hasher.Calls);
        Assert.Equal(0, store.AddCalls);
        Assert.Equal(0, store.SaveCalls);
    }

    [Fact]
    public async Task HandleAsync_StoresHasherOutputInsteadOfPlainPassword()
    {
        // Arrange
        var store = new FakeStore();
        var hasher = new FakeHasher();
        var handler = new RegisterUserHandler(store, hasher, new FakeAccountNumberGenerator());

        // Act
        await handler.HandleAsync(new("customer@example.com", ValidPassword));

        // Assert
        Assert.Equal(1, hasher.Calls);
        Assert.Equal(ValidPassword, hasher.ReceivedPassword);
        Assert.Equal(hasher.Output, store.User!.PasswordHash);
        Assert.NotEqual(ValidPassword, store.User.PasswordHash);
    }

    [Fact]
    public async Task HandleAsync_SavesExactlyOnceAfterAddingUserAndAccount()
    {
        // Arrange
        var store = new FakeStore();
        var handler = new RegisterUserHandler(store, new FakeHasher(), new FakeAccountNumberGenerator());
        using var cancellation = new CancellationTokenSource();

        // Act
        await handler.HandleAsync(new("customer@example.com", ValidPassword), cancellation.Token);

        // Assert
        Assert.Equal(1, store.SaveCalls);
        Assert.Equal(new[] { "lookup", "add", "save" }, store.Operations);
        Assert.All(store.Tokens, token => Assert.Equal(cancellation.Token, token));
    }

    [Fact]
    public async Task HandleAsync_ResultContainsOnlySafeFields()
    {
        // Arrange
        var hasher = new FakeHasher();
        var handler = new RegisterUserHandler(new FakeStore(), hasher, new FakeAccountNumberGenerator());

        // Act
        var result = await handler.HandleAsync(new("customer@example.com", ValidPassword));

        // Assert
        var json = JsonSerializer.Serialize(result);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(new[] { "AccountId", "AccountNumber", "Email", "UserId" },
            document.RootElement.EnumerateObject().Select(property => property.Name).OrderBy(name => name));
        Assert.DoesNotContain(ValidPassword, json);
        Assert.DoesNotContain(hasher.Output, json);
    }

    private sealed class FakeStore : IUserRegistrationStore
    {
        public bool EmailExists { get; init; }
        public string? LookupEmail { get; private set; }
        public User? User { get; private set; }
        public Account? Account { get; private set; }
        public int LookupCalls { get; private set; }
        public int AddCalls { get; private set; }
        public int SaveCalls { get; private set; }
        public List<string> Operations { get; } = new();
        public List<CancellationToken> Tokens { get; } = new();

        public Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken cancellationToken)
        {
            LookupCalls++;
            LookupEmail = normalizedEmail;
            Operations.Add("lookup");
            Tokens.Add(cancellationToken);
            return Task.FromResult(EmailExists);
        }

        public Task AddAsync(User user, Account account, CancellationToken cancellationToken)
        {
            AddCalls++;
            User = user;
            Account = account;
            Operations.Add("add");
            Tokens.Add(cancellationToken);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            SaveCalls++;
            Operations.Add("save");
            Tokens.Add(cancellationToken);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeHasher : IPasswordHasher
    {
        public string Output { get; } = "fake-hash-for-test";
        public string? ReceivedPassword { get; private set; }
        public int Calls { get; private set; }

        public string Hash(string password)
        {
            Calls++;
            ReceivedPassword = password;
            return Output;
        }

        public bool Verify(string passwordHash, string providedPassword) => throw new NotSupportedException();
    }

    private sealed class FakeAccountNumberGenerator : IAccountNumberGenerator
    {
        public string Number { get; } = "FG000000000001";
        public int Calls { get; private set; }

        public string Generate()
        {
            Calls++;
            return Number;
        }
    }
}
