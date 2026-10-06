using FinCore.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinCore.Infrastructure.Persistence.Configurations;

public sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("idempotency_records");
        builder.HasKey(record => record.Id);
        builder.Property(record => record.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(record => record.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(record => record.Operation).HasColumnName("operation").HasMaxLength(100).IsRequired();
        builder.Property(record => record.Key).HasColumnName("key").HasMaxLength(128).IsRequired();
        builder.Property(record => record.RequestHash).HasColumnName("request_hash").HasMaxLength(64).IsRequired();
        builder.Property(record => record.ResponsePayload).HasColumnName("response_payload").HasColumnType("jsonb").IsRequired();
        builder.Property(record => record.StatusCode).HasColumnName("status_code").IsRequired();
        builder.Property(record => record.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(record => record.ExpiresAtUtc).HasColumnName("expires_at_utc").HasColumnType("timestamp with time zone").IsRequired();

        builder.HasOne<User>().WithMany().HasForeignKey(record => record.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(record => new { record.UserId, record.Operation, record.Key }).IsUnique();
        builder.HasIndex(record => record.ExpiresAtUtc);
    }
}
