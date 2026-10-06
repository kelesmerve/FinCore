using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FinCore.Application.Abstractions.Persistence;
using FinCore.Application.Features.Transfers.Transfer;
using Microsoft.EntityFrameworkCore;

namespace FinCore.Infrastructure.Persistence.Repositories;

public sealed class EfIdempotentTransferStore(FinCoreDbContext context) : IIdempotentTransferStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IdempotentTransferResult> ExecuteAsync(
        Guid userId, string operation, string key, string requestHash,
        Func<CancellationToken, Task<TransferMoneyResult>> transfer,
        CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var lockMaterial = Encoding.UTF8.GetBytes($"{userId:N}|{operation}|{key}");
            var lockHash = SHA256.HashData(lockMaterial);
            var lockId = BinaryPrimitives.ReadInt64BigEndian(lockHash.AsSpan(0, 8));
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({lockId})", cancellationToken);

            var now = DateTime.UtcNow;
            var existing = await context.IdempotencyRecords.AsNoTracking()
                .SingleOrDefaultAsync(record => record.UserId == userId &&
                    record.Operation == operation && record.Key == key, cancellationToken);

            if (existing is not null && existing.ExpiresAtUtc > now)
            {
                if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
                    throw new IdempotencyConflictException();
                var replay = JsonSerializer.Deserialize<TransferMoneyResult>(existing.ResponsePayload, JsonOptions)
                    ?? throw new InvalidOperationException("Stored transfer response is invalid.");
                await transaction.CommitAsync(cancellationToken);
                return new IdempotentTransferResult(replay, true);
            }

            if (existing is not null)
                await context.IdempotencyRecords.Where(record => record.Id == existing.Id)
                    .ExecuteDeleteAsync(cancellationToken);

            var result = await transfer(cancellationToken);
            var payload = JsonSerializer.Serialize(result, JsonOptions);
            context.IdempotencyRecords.Add(new IdempotencyRecord(
                userId, operation, key, requestHash, payload, 200, now, now.AddHours(24)));
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new IdempotentTransferResult(result, false);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}
