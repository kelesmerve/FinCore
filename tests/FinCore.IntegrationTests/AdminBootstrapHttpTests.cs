using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinCore.Application.Features.Users.BootstrapAdmin;
using FinCore.Application.Security;
using FinCore.Domain.Entities;
using FinCore.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FinCore.IntegrationTests;

public sealed class AdminBootstrapHttpTests
{
    [Fact]
    public async Task BootstrappedAdmin_CanLoginAndAccessAdminOnlyWithoutFinancialAccount()
    {
        using var factory = new AdminBootstrapFactory();
        using var client = factory.CreateClient();
        var email = $"bootstrap-{Guid.NewGuid():N}@example.com";
        var password = $"T3st-{Guid.NewGuid():N}!";
        try
        {
            using (var scope = factory.Services.CreateScope())
            {
                var handler = scope.ServiceProvider.GetRequiredService<BootstrapAdminHandler>();
                Assert.Equal(BootstrapAdminResult.Created,
                    await handler.HandleAsync($" {email.ToUpperInvariant()} ", password));
                Assert.Equal(BootstrapAdminResult.AlreadyExists,
                    await handler.HandleAsync(email, password));
            }

            await using (var context = factory.CreateDbContext())
            {
                var admin = await context.Users.SingleAsync(user => user.Email == email);
                Assert.Equal(UserRole.Admin, admin.Role);
                Assert.True(admin.IsActive);
                Assert.NotEqual(password, admin.PasswordHash);
                using var scope = factory.Services.CreateScope();
                var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
                Assert.True(hasher.Verify(admin.PasswordHash, password));
                Assert.False(await context.Accounts.AnyAsync(account => account.UserId == admin.Id));
                Assert.Equal(1, await context.Users.CountAsync(user => user.Email == email));
            }

            using var login = await client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            using var loginJson = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
            var token = loginJson.RootElement.GetProperty("accessToken").GetString();
            Assert.False(string.IsNullOrWhiteSpace(token));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var adminResponse = await client.GetAsync("/api/admin/users?page=1&pageSize=20");
            Assert.Equal(HttpStatusCode.OK, adminResponse.StatusCode);
            var response = await adminResponse.Content.ReadAsStringAsync();
            Assert.DoesNotContain("passwordHash", response, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(password, response);
            using var adminJson = JsonDocument.Parse(response);
            Assert.True(adminJson.RootElement.GetProperty("totalCount").GetInt32() >= 1);
        }
        finally { await factory.CleanupAsync(email); }
    }

    private sealed class AdminBootstrapFactory : WebApplicationFactory<Program>
    {
        private readonly string _connectionString = new ConfigurationBuilder()
            .AddUserSecrets<AdminBootstrapHttpTests>(optional: true).AddEnvironmentVariables().Build()
            .GetConnectionString("FinCoreDatabase")
            ?? throw new InvalidOperationException("Configure ConnectionStrings:FinCoreDatabase for HTTP tests.");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddUserSecrets<AdminBootstrapHttpTests>(optional: true).AddEnvironmentVariables());
            builder.UseSetting("ConnectionStrings:FinCoreDatabase", _connectionString);
        }

        public FinCoreDbContext CreateDbContext() => new(
            new DbContextOptionsBuilder<FinCoreDbContext>().UseNpgsql(_connectionString).Options);

        public async Task CleanupAsync(string email)
        {
            await using var context = CreateDbContext();
            await using var transaction = await context.Database.BeginTransactionAsync();
            var ids = await context.Users.Where(user => user.Email == email).Select(user => user.Id).ToArrayAsync();
            await context.RefreshTokens.Where(token => ids.Contains(token.UserId)).ExecuteDeleteAsync();
            await context.Users.Where(user => ids.Contains(user.Id)).ExecuteDeleteAsync();
            await transaction.CommitAsync();
        }
    }
}
