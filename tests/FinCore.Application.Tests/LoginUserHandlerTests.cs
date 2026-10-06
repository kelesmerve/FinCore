using System.Text.Json;
using FinCore.Application.Abstractions.Persistence;
using FinCore.Application.Features.Users.Login;
using FinCore.Application.Security;
using FinCore.Domain.Entities;
using Xunit;

namespace FinCore.Application.Tests;

public class LoginUserHandlerTests
{
    [Fact]
    public async Task HandleAsync_WithValidCredentials_ReturnsTokensAndPersistsRefreshHash()
    {
        // Arrange
        var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();

        // Act
        var result = await fixture.Handler.HandleAsync(new("customer@example.com", "provided-password"), cancellation.Token);

        // Assert
        Assert.Equal(fixture.Tokens.Result.Token, result.AccessToken);
        Assert.Equal(fixture.Tokens.Result.ExpiresAtUtc, result.AccessTokenExpiresAtUtc);
        Assert.Equal(cancellation.Token, fixture.Store.Token);
        Assert.Equal(fixture.Store.User!.PasswordHash, fixture.Hasher.HashReceived);
        Assert.Equal("provided-password", fixture.Hasher.PasswordReceived);
        Assert.Same(fixture.Store.User, fixture.Tokens.UserReceived);
        Assert.Equal(1, fixture.Tokens.Calls);
        Assert.Equal(1, fixture.RefreshGenerator.Calls);
        Assert.Equal(1, fixture.RefreshStore.AddCalls);
        Assert.Equal(1, fixture.RefreshStore.SaveCalls);
        Assert.Equal(cancellation.Token, fixture.RefreshStore.AddToken);
        Assert.Equal(cancellation.Token, fixture.RefreshStore.SaveToken);
        var saved = Assert.IsType<RefreshToken>(fixture.RefreshStore.Saved);
        Assert.Equal(fixture.Store.User.Id, saved.UserId);
        Assert.Equal(fixture.RefreshGenerator.Material.TokenHash, saved.TokenHash);
        Assert.NotEqual(fixture.RefreshGenerator.Material.RawToken, saved.TokenHash);
        Assert.Equal(fixture.RefreshGenerator.Material.RawToken, result.RefreshToken);
        Assert.Equal(saved.ExpiresAtUtc, result.RefreshTokenExpiresAtUtc);
        Assert.Equal(TimeSpan.FromDays(7), saved.ExpiresAtUtc - saved.CreatedAtUtc);
        Assert.Equal(DateTimeKind.Utc, saved.CreatedAtUtc.Kind);
        Assert.Equal(DateTimeKind.Utc, saved.ExpiresAtUtc.Kind);
        Assert.InRange(saved.CreatedAtUtc, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow);
    }

    [Fact]
    public async Task HandleAsync_NormalizesEmail()
    {
        // Arrange
        var fixture = new Fixture();

        // Act
        await fixture.Handler.HandleAsync(new("  CUSTOMER@Example.COM  ", "provided-password"));

        // Assert
        Assert.Equal("customer@example.com", fixture.Store.EmailReceived);
    }

    [Fact]
    public async Task HandleAsync_WithUnknownEmail_ThrowsInvalidCredentialsWithoutToken()
    {
        // Arrange
        var fixture = new Fixture();
        fixture.Store.User = null;

        // Act
        var act = () => fixture.Handler.HandleAsync(new("unknown@example.com", "provided-password"));

        // Assert
        var exception = await Assert.ThrowsAsync<InvalidCredentialsException>(act);
        Assert.Equal("Invalid email or password.", exception.Message);
        Assert.Equal(0, fixture.Hasher.Calls);
        Assert.Equal(0, fixture.Tokens.Calls);
        Assert.Equal(0, fixture.RefreshGenerator.Calls);
        Assert.Equal(0, fixture.RefreshStore.AddCalls);
        Assert.Equal(0, fixture.RefreshStore.SaveCalls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task HandleAsync_WithWrongPassword_DoesNotRevealActiveStatus(bool active)
    {
        // Arrange
        var fixture = new Fixture();
        fixture.Hasher.Valid = false;
        if (!active) fixture.Store.User!.Deactivate();

        // Act
        var act = () => fixture.Handler.HandleAsync(new("customer@example.com", "wrong-password"));

        // Assert
        var exception = await Assert.ThrowsAsync<InvalidCredentialsException>(act);
        Assert.Equal("Invalid email or password.", exception.Message);
        Assert.Equal(1, fixture.Hasher.Calls);
        Assert.Equal(0, fixture.Tokens.Calls);
        Assert.Equal(0, fixture.RefreshGenerator.Calls);
        Assert.Equal(0, fixture.RefreshStore.AddCalls);
        Assert.Equal(0, fixture.RefreshStore.SaveCalls);
    }

    [Fact]
    public async Task HandleAsync_WithInactiveUserAndCorrectPassword_ThrowsInactiveUserWithoutToken()
    {
        // Arrange
        var fixture = new Fixture();
        fixture.Store.User!.Deactivate();

        // Act
        var act = () => fixture.Handler.HandleAsync(new("customer@example.com", "provided-password"));

        // Assert
        await Assert.ThrowsAsync<InactiveUserException>(act);
        Assert.Equal(1, fixture.Hasher.Calls);
        Assert.Equal(0, fixture.Tokens.Calls);
        Assert.Equal(0, fixture.RefreshGenerator.Calls);
        Assert.Equal(0, fixture.RefreshStore.AddCalls);
        Assert.Equal(0, fixture.RefreshStore.SaveCalls);
    }

    [Theory]
    [InlineData(null, "password")]
    [InlineData("", "password")]
    [InlineData("   ", "password")]
    [InlineData("customer@example.com", null)]
    [InlineData("customer@example.com", "")]
    [InlineData("customer@example.com", "   ")]
    public async Task HandleAsync_WithEmptyCredentials_RejectsBeforeLookup(string? email, string? password)
    {
        // Arrange
        var fixture = new Fixture();

        // Act
        var act = () => fixture.Handler.HandleAsync(new(email!, password!));

        // Assert
        await Assert.ThrowsAsync<InvalidCredentialsException>(act);
        Assert.Equal(0, fixture.Store.Calls);
        Assert.Equal(0, fixture.Hasher.Calls);
        Assert.Equal(0, fixture.Tokens.Calls);
        Assert.Equal(0, fixture.RefreshGenerator.Calls);
        Assert.Equal(0, fixture.RefreshStore.AddCalls);
        Assert.Equal(0, fixture.RefreshStore.SaveCalls);
    }

    [Fact]
    public async Task HandleAsync_ResultContainsOnlyTokensAndExpirations()
    {
        // Arrange
        var fixture = new Fixture();
        const string password = "provided-password";

        // Act
        var result = await fixture.Handler.HandleAsync(new("customer@example.com", password));

        // Assert
        var json = JsonSerializer.Serialize(result);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(new[] { "AccessToken", "AccessTokenExpiresAtUtc", "RefreshToken", "RefreshTokenExpiresAtUtc" },
            document.RootElement.EnumerateObject().Select(property => property.Name).OrderBy(name => name));
        Assert.DoesNotContain(fixture.RefreshGenerator.Material.TokenHash, json);
        Assert.DoesNotContain(password, json);
        Assert.DoesNotContain(fixture.Store.User!.PasswordHash, json);
        Assert.DoesNotContain(fixture.Store.User.Email, json);
    }

    private sealed class Fixture
    {
        public FakeStore Store { get; } = new();
        public FakeHasher Hasher { get; } = new();
        public FakeTokens Tokens { get; } = new();
        public FakeRefreshGenerator RefreshGenerator { get; } = new();
        public FakeRefreshStore RefreshStore { get; } = new();
        public LoginUserHandler Handler => new(Store, Hasher, Tokens, RefreshGenerator, RefreshStore);
    }

    [Fact]
    public async Task HandleAsync_SaveFailure_DoesNotReturnTokens()
    {
        // Arrange
        var fixture = new Fixture();
        fixture.RefreshStore.FailSave = true;
        // Act
        var act = () => fixture.Handler.HandleAsync(new("customer@example.com", "provided-password"));
        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.Equal(1, fixture.RefreshStore.SaveCalls);
    }

    private sealed class FakeRefreshGenerator : IRefreshTokenGenerator
    {
        public RefreshTokenMaterial Material { get; } = new("fake-raw-refresh", "fake-refresh-hash");
        public int Calls { get; private set; }
        public RefreshTokenMaterial Generate() { Calls++; return Material; }
        public string Hash(string rawToken) => throw new NotSupportedException();
    }

    private sealed class FakeRefreshStore : IRefreshTokenStore
    {
        public Task<IReadOnlyCollection<RefreshToken>> FindUnrevokedFamilyAsync(Guid userId, Guid familyId, CancellationToken ct) => throw new NotSupportedException();
        public Task<RefreshToken?> FindByHashAsync(string hash, CancellationToken ct) => throw new NotSupportedException();
        public Task<User?> FindUserAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public RefreshToken? Saved { get; private set; }
        public int AddCalls { get; private set; }
        public int SaveCalls { get; private set; }
        public CancellationToken AddToken { get; private set; }
        public CancellationToken SaveToken { get; private set; }
        public bool FailSave { get; set; }
        public Task AddAsync(RefreshToken token, CancellationToken cancellationToken)
        {
            Saved = token;
            AddCalls++;
            AddToken = cancellationToken;
            return Task.CompletedTask;
        }
        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            SaveCalls++;
            SaveToken = cancellationToken;
            if (FailSave) throw new InvalidOperationException("Simulated persistence failure.");
            return Task.CompletedTask;
        }
    }
    private sealed class FakeStore : IUserAuthenticationStore
    {
        public User? User { get; set; } = FinCore.Domain.Entities.User.CreateCustomer("customer@example.com", "stored-test-hash");
        public string? EmailReceived { get; private set; }
        public CancellationToken Token { get; private set; }
        public int Calls { get; private set; }

        public Task<User?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken)
        {
            Calls++;
            EmailReceived = normalizedEmail;
            Token = cancellationToken;
            return Task.FromResult(User);
        }
    }

    private sealed class FakeHasher : IPasswordHasher
    {
        public bool Valid { get; set; } = true;
        public int Calls { get; private set; }
        public string? HashReceived { get; private set; }
        public string? PasswordReceived { get; private set; }
        public string Hash(string password) => throw new NotSupportedException();

        public bool Verify(string passwordHash, string providedPassword)
        {
            Calls++;
            HashReceived = passwordHash;
            PasswordReceived = providedPassword;
            return Valid;
        }
    }

    private sealed class FakeTokens : IJwtTokenGenerator
    {
        public JwtTokenResult Result { get; } = new("fake-access-token", new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        public int Calls { get; private set; }
        public User? UserReceived { get; private set; }

        public JwtTokenResult Generate(User user)
        {
            Calls++;
            UserReceived = user;
            return Result;
        }
    }
}
