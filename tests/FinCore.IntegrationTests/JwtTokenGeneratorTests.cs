using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using FinCore.Domain.Entities;
using FinCore.Infrastructure;
using FinCore.Infrastructure.Security;
using FinCore.Application.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace FinCore.IntegrationTests;

public class JwtTokenGeneratorTests
{
    [Fact]
    public void Generate_ProducesValidSignedTokenWithExpectedClaimsAndExpiration()
    {
        // Arrange
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Issuer"] = "FinCore",
            ["Audience"] = "FinCore.Client",
            ["SecretKey"] = key,
            ["AccessTokenExpirationMinutes"] = "15"
        }).Build();
        var services = new ServiceCollection();
        services.AddInfrastructure("Host=localhost;Database=unused", configuration);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var generator = scope.ServiceProvider.GetRequiredService<IJwtTokenGenerator>();
        var user = User.CreateCustomer("jwt-test@example.com", "test-only-hash");
        var before = DateTime.UtcNow;

        // Act
        var result = generator.Generate(user);
        var second = generator.Generate(user);
        var after = DateTime.UtcNow;

        // Assert
        Assert.False(string.IsNullOrWhiteSpace(result.Token));
        Assert.Equal(DateTimeKind.Utc, result.ExpiresAtUtc.Kind);
        Assert.InRange(result.ExpiresAtUtc, before.AddMinutes(15), after.AddMinutes(15));
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var principal = handler.ValidateToken(result.Token, new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            ValidateIssuer = true, ValidIssuer = "FinCore",
            ValidateAudience = true, ValidAudience = "FinCore.Client",
            ValidateLifetime = true, RequireExpirationTime = true, RequireSignedTokens = true,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ClockSkew = TimeSpan.Zero
        }, out var validated);
        Assert.Equal(user.Id.ToString(), principal.FindFirst("sub")?.Value);
        Assert.Equal(user.Email, principal.FindFirst("email")?.Value);
        Assert.Equal("Customer", principal.FindFirst("role")?.Value);
        Assert.True(Guid.TryParse(principal.FindFirst("jti")?.Value, out _));
        Assert.NotEqual(principal.FindFirst("jti")?.Value,
            handler.ReadJwtToken(second.Token).Id);
        Assert.InRange((result.ExpiresAtUtc - validated.ValidTo).TotalSeconds, 0, 1);
        var jwt = Assert.IsType<JwtSecurityToken>(validated);
        Assert.Equal(new[] { "aud", "email", "exp", "iss", "jti", "nbf", "role", "sub" },
            jwt.Payload.Keys.OrderBy(value => value));
        Assert.DoesNotContain(user.PasswordHash, jwt.Payload.SerializeToJson());
    }

    [Theory]
    [InlineData("")]
    [InlineData("too-short")]
    [InlineData("1234567890123456789012345678901")]
    public void Constructor_RejectsInsufficientSecretKey(string secret)
    {
        // Arrange
        var options = Options.Create(new JwtOptions
        {
            Issuer = "FinCore", Audience = "FinCore.Client",
            SecretKey = secret, AccessTokenExpirationMinutes = 15
        });

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() => new JwtTokenGenerator(options));

        // Assert
        Assert.Contains("32 UTF-8 bytes", exception.Message);
    }
}
