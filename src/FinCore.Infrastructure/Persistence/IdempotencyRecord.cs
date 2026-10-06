using System.Text.Json;

namespace FinCore.Infrastructure.Persistence;

public sealed class IdempotencyRecord
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string Operation { get; private set; } = null!;
    public string Key { get; private set; } = null!;
    public string RequestHash { get; private set; } = null!;
    public string ResponsePayload { get; private set; } = null!;
    public int StatusCode { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }

    private IdempotencyRecord() { }

    public IdempotencyRecord(
        Guid userId, string operation, string key, string requestHash,
        string responsePayload, int statusCode, DateTime createdAtUtc, DateTime expiresAtUtc)
    {
        if (userId == Guid.Empty) throw new ArgumentException("User ID is required.", nameof(userId));
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(responsePayload);
        if (operation.Length > 100) throw new ArgumentOutOfRangeException(nameof(operation));
        if (key.Length > 128) throw new ArgumentOutOfRangeException(nameof(key));
        if (requestHash.Length != 64 || !requestHash.All(Uri.IsHexDigit))
            throw new ArgumentException("Request hash must be a SHA-256 hexadecimal value.", nameof(requestHash));
        using (JsonDocument.Parse(responsePayload)) { }
        if (createdAtUtc.Kind != DateTimeKind.Utc || expiresAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Dates must be UTC.");
        if (expiresAtUtc <= createdAtUtc)
            throw new ArgumentException("Expiration must follow creation.", nameof(expiresAtUtc));

        Id = Guid.NewGuid();
        UserId = userId;
        Operation = operation;
        Key = key;
        RequestHash = requestHash;
        ResponsePayload = responsePayload;
        StatusCode = statusCode;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
    }
}
