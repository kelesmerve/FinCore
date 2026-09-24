using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FinCore.Application.Security;
using FinCore.Domain.Entities;
using FinCore.Infrastructure.Persistence;
using FinCore.Infrastructure.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace FinCore.IntegrationTests;

[CollectionDefinition("User listing database", DisableParallelization = true)]
public sealed class UserListingDatabaseCollection { }

[Collection("User listing database")]
public class AdminUsersHttpTests
{
    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData(RoleNames.Customer, HttpStatusCode.Forbidden)]
    public async Task ListUsers_WithoutAdminRole_DeniesAccess(string? role, HttpStatusCode expected)
    {
        using var factory = new AdminFactory();
        using var client = factory.CreateClient();
        if (role is not null) factory.Authenticate(client, role);
        using var response = await client.GetAsync("/api/admin/users");
        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task ListUsers_Admin_ReturnsSafeOrderedPagesAndTotalCount()
    {
        using var factory = new AdminFactory();
        using var client = factory.CreateClient();
        factory.Authenticate(client, RoleNames.Admin);
        await using var context = factory.CreateDbContext();
        var users = Enumerable.Range(0, 3).Select(_ =>
            User.CreateCustomer($"listing-{Guid.NewGuid():N}@example.com", "listing-test-only-hash")).ToArray();
        var ids = users.Select(u => u.Id).ToArray();
        try
        {
            context.Users.AddRange(users);
            await context.SaveChangesAsync();
            // Give the new users a shared timestamp to exercise the Id tie-breaker.
            var timestamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            await context.Users.Where(u => ids.Contains(u.Id))
                .ExecuteUpdateAsync(setters => setters.SetProperty(u => u.CreatedAtUtc, timestamp));
            var expectedIds = await context.Users.AsNoTracking()
                .OrderBy(u => u.CreatedAtUtc).ThenBy(u => u.Id).Select(u => u.Id).ToArrayAsync();

            using var defaultResponse = await client.GetAsync("/api/admin/users");
            Assert.Equal(HttpStatusCode.OK, defaultResponse.StatusCode);
            using var defaults = JsonDocument.Parse(await defaultResponse.Content.ReadAsStringAsync());
            Assert.Equal(1, defaults.RootElement.GetProperty("page").GetInt32());
            Assert.Equal(20, defaults.RootElement.GetProperty("pageSize").GetInt32());

            var actualIds = new List<Guid>();
            var pageCount = (expectedIds.Length + 1) / 2;
            for (var page = 1; page <= pageCount + 1; page++)
            {
                using var response = await client.GetAsync($"/api/admin/users?page={page}&pageSize=2");
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var body = await response.Content.ReadAsStringAsync();
                Assert.False(body.Contains("password", StringComparison.OrdinalIgnoreCase));
                Assert.DoesNotContain("listing-test-only-hash", body);
                using var json = JsonDocument.Parse(body);
                var root = json.RootElement;
                Assert.Equal(new[] { "items", "page", "pageSize", "totalCount" },
                    root.EnumerateObject().Select(p => p.Name).OrderBy(n => n));
                Assert.Equal(expectedIds.Length, root.GetProperty("totalCount").GetInt32());
                Assert.Equal(page, root.GetProperty("page").GetInt32());
                Assert.Equal(2, root.GetProperty("pageSize").GetInt32());
                var items = root.GetProperty("items").EnumerateArray().ToArray();
                Assert.Equal(expectedIds.Skip((page - 1) * 2).Take(2), items.Select(i => i.GetProperty("id").GetGuid()));
                foreach (var item in items)
                {
                    Assert.Equal(new[] { "createdAtUtc", "email", "id", "isActive", "role" },
                        item.EnumerateObject().Select(p => p.Name).OrderBy(n => n));
                    actualIds.Add(item.GetProperty("id").GetGuid());
                }
            }
            Assert.Equal(expectedIds, actualIds);
        }
        finally
        {
            await context.Users.Where(u => ids.Contains(u.Id)).ExecuteDeleteAsync();
        }
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    [InlineData(-1, 20)]
    public async Task ListUsers_AdminWithInvalidPagination_Returns400(int page, int pageSize)
    {
        using var factory = new AdminFactory();
        using var client = factory.CreateClient();
        factory.Authenticate(client, RoleNames.Admin);
        using var response = await client.GetAsync($"/api/admin/users?page={page}&pageSize={pageSize}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(400, json.RootElement.GetProperty("status").GetInt32());
    }

    private sealed class AdminFactory : WebApplicationFactory<Program>
    {
        private readonly string _connectionString = new ConfigurationBuilder()
            .AddUserSecrets<AdminUsersHttpTests>(optional: true).AddEnvironmentVariables().Build()
            .GetConnectionString("FinCoreDatabase")
            ?? throw new InvalidOperationException("Configure ConnectionStrings:FinCoreDatabase for HTTP tests.");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddUserSecrets<AdminUsersHttpTests>(optional: true).AddEnvironmentVariables());
            builder.UseSetting("ConnectionStrings:FinCoreDatabase", _connectionString);
        }

        public void Authenticate(HttpClient client, string role)
        {
            var options = Services.GetRequiredService<IOptions<JwtOptions>>().Value;
            var now = DateTime.UtcNow;
            var token = new JwtSecurityToken(options.Issuer, options.Audience,
                [
                    new Claim("sub", Guid.NewGuid().ToString()),
                    new Claim("email", "rbac-test@example.com"),
                    new Claim("role", role),
                    new Claim("jti", Guid.NewGuid().ToString())
                ], now, now.AddMinutes(5),
                new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SecretKey)),
                    SecurityAlgorithms.HmacSha256));
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        }

        public FinCoreDbContext CreateDbContext() => new(
            new DbContextOptionsBuilder<FinCoreDbContext>().UseNpgsql(_connectionString).Options);
    }
}
