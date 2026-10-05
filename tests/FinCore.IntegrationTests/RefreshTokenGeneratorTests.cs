using System.Text.RegularExpressions;
using FinCore.Application.Security;
using FinCore.Infrastructure;
using FinCore.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FinCore.IntegrationTests;

public class RefreshTokenGeneratorTests
{
    [Fact]
    public void Generate_ReturnsDistinctRawTokenAndHashWithExpectedFormats()
    {
        // Arrange
        var generator = new RefreshTokenGenerator();

        // Act
        var material = generator.Generate();

        // Assert
        Assert.False(string.IsNullOrWhiteSpace(material.RawToken));
        Assert.False(string.IsNullOrWhiteSpace(material.TokenHash));
        Assert.False(material.RawToken == material.TokenHash);
        var isUrlSafe = Regex.IsMatch(material.RawToken, "^[A-Za-z0-9_-]+$");
        Assert.True(isUrlSafe);
        var isCanonicalHash = Regex.IsMatch(material.TokenHash, "^[0-9A-F]{64}$");
        Assert.True(isCanonicalHash);
        var base64 = material.RawToken.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight((base64.Length + 3) / 4 * 4, '=');
        Assert.True(Convert.FromBase64String(base64).Length >= 32);
    }

    [Fact]
    public void Generate_ConsecutiveCalls_ProduceDifferentTokens()
    {
        // Arrange
        var generator = new RefreshTokenGenerator();

        // Act
        var first = generator.Generate();
        var second = generator.Generate();

        // Assert
        Assert.False(first.RawToken == second.RawToken);
        Assert.False(first.TokenHash == second.TokenHash);
    }

    [Fact]
    public void Hash_SameInput_IsDeterministicAndMatchesGeneratedHash()
    {
        // Arrange
        var generator = new RefreshTokenGenerator();
        var material = generator.Generate();

        // Act
        var first = generator.Hash(material.RawToken);
        var second = new RefreshTokenGenerator().Hash(material.RawToken);

        // Assert
        Assert.True(first == second);
        Assert.True(material.TokenHash == first);
    }

    [Fact]
    public void Hash_UsesCanonicalSha256()
    {
        // Arrange
        var generator = new RefreshTokenGenerator();

        // Act
        var hash = generator.Hash("abc");

        // Assert
        Assert.True(hash == "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\r\n")]
    public void Hash_EmptyInput_IsRejected(string? rawToken)
    {
        // Arrange
        var generator = new RefreshTokenGenerator();

        // Act
        var act = () => generator.Hash(rawToken!);

        // Assert
        Assert.ThrowsAny<ArgumentException>(act);
    }

    [Fact]
    public void AddInfrastructure_RegistersGeneratorAsSingleton()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddInfrastructure("Host=localhost;Database=unused", new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        // Act
        var firstGenerator = first.ServiceProvider.GetRequiredService<IRefreshTokenGenerator>();
        var secondGenerator = second.ServiceProvider.GetRequiredService<IRefreshTokenGenerator>();

        // Assert
        Assert.IsType<RefreshTokenGenerator>(firstGenerator);
        Assert.Same(firstGenerator, secondGenerator);
    }
}
