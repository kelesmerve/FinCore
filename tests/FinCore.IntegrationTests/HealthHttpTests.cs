using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FinCore.IntegrationTests;

public sealed class HealthHttpTests
{
    [Fact]
    public async Task Live_WithoutAuthentication_Returns200WithoutDatabase()
    {
        using var factory = new HealthFactory(unreachableDatabase: true);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Ready_WhenPostgresReachable_Returns200()
    {
        using var factory = new HealthFactory(unreachableDatabase: false);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Ready_WhenPostgresUnreachable_Returns503WithoutDetails()
    {
        using var factory = new HealthFactory(unreachableDatabase: true);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", await response.Content.ReadAsStringAsync());
    }

    private sealed class HealthFactory(bool unreachableDatabase) : WebApplicationFactory<Program>
    {
        private readonly string _connectionString = unreachableDatabase
            ? "Host=127.0.0.1;Port=1;Database=unreachable;Username=unreachable;Timeout=1"
            : new ConfigurationBuilder()
                .AddUserSecrets<HealthHttpTests>(optional: true).AddEnvironmentVariables().Build()
                .GetConnectionString("FinCoreDatabase")
                ?? throw new InvalidOperationException("Configure ConnectionStrings:FinCoreDatabase for HTTP tests.");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddUserSecrets<HealthHttpTests>(optional: true).AddEnvironmentVariables());
            builder.UseSetting("ConnectionStrings:FinCoreDatabase", _connectionString);
        }
    }
}
