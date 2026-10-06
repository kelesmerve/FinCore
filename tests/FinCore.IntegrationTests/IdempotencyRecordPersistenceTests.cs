using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FinCore.Domain.Entities;
using FinCore.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FinCore.IntegrationTests;

public sealed class IdempotencyRecordPersistenceTests
{
    private static FinCoreDbContext CreateContext()
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets<IdempotencyRecordPersistenceTests>(optional: true).AddEnvironmentVariables().Build();
        var connection = configuration.GetConnectionString("FinCoreDatabase")
            ?? throw new InvalidOperationException("Configure ConnectionStrings:FinCoreDatabase for integration tests.");
        return new(new DbContextOptionsBuilder<FinCoreDbContext>().UseNpgsql(connection).Options);
    }

    [Fact]
    public async Task Records_ReloadAndEnforceUserOperationKeyUniqueness()
    {
        var firstUser = User.CreateCustomer($"idempotency-{Guid.NewGuid():N}@example.com", "test-hash");
        var secondUser = User.CreateCustomer($"idempotency-{Guid.NewGuid():N}@example.com", "test-hash");
        var key = Guid.NewGuid().ToString("N");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("synthetic-test-request")));
        var created = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        const string response = "{\"transactionId\":\"00000000-0000-0000-0000-000000000001\"}";
        var first = new IdempotencyRecord(firstUser.Id, "transfer", key, hash, response, 200, created, created.AddDays(1));
        var anotherOperation = new IdempotencyRecord(firstUser.Id, "another-operation", key, hash, response, 200, created, created.AddDays(1));
        var anotherUser = new IdempotencyRecord(secondUser.Id, "transfer", key, hash, response, 200, created, created.AddDays(1));
        try
        {
            await using (var context = CreateContext())
            {
                context.Users.AddRange(firstUser, secondUser);
                context.IdempotencyRecords.AddRange(first, anotherOperation, anotherUser);
                await context.SaveChangesAsync();
                context.ChangeTracker.Clear();
                var loaded = await context.IdempotencyRecords.SingleAsync(r => r.Id == first.Id);
                Assert.Equal(first.UserId, loaded.UserId);
                Assert.Equal(first.Operation, loaded.Operation);
                Assert.Equal(key, loaded.Key);
                Assert.Equal(hash, loaded.RequestHash);
                using var payload = JsonDocument.Parse(loaded.ResponsePayload);
                Assert.Equal("00000000-0000-0000-0000-000000000001",
                    payload.RootElement.GetProperty("transactionId").GetString());
                Assert.Equal(200, loaded.StatusCode);
                Assert.Equal(created, loaded.CreatedAtUtc);
                Assert.Equal(created.AddDays(1), loaded.ExpiresAtUtc);
                Assert.Equal(DateTimeKind.Utc, loaded.CreatedAtUtc.Kind);
                Assert.Equal(DateTimeKind.Utc, loaded.ExpiresAtUtc.Kind);
            }

            await using (var duplicateContext = CreateContext())
            {
                duplicateContext.IdempotencyRecords.Add(new IdempotencyRecord(
                    firstUser.Id, "transfer", key, hash, response, 200, created, created.AddDays(1)));
                await Assert.ThrowsAsync<DbUpdateException>(() => duplicateContext.SaveChangesAsync());
            }

            await using var verify = CreateContext();
            Assert.Equal(3, await verify.IdempotencyRecords.CountAsync(r =>
                r.UserId == firstUser.Id || r.UserId == secondUser.Id));
        }
        finally
        {
            await using var cleanup = CreateContext();
            await using var transaction = await cleanup.Database.BeginTransactionAsync();
            var userIds = new[] { firstUser.Id, secondUser.Id };
            await cleanup.IdempotencyRecords.Where(r => userIds.Contains(r.UserId)).ExecuteDeleteAsync();
            await cleanup.Users.Where(u => userIds.Contains(u.Id)).ExecuteDeleteAsync();
            await transaction.CommitAsync();
        }
    }
}
