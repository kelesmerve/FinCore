using FinCore.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinCore.Infrastructure.Persistence.Configurations;

public sealed class LedgerEntryConfiguration : IEntityTypeConfiguration<LedgerEntry>
{
    public void Configure(EntityTypeBuilder<LedgerEntry> builder)
    {
        builder.ToTable("ledger_entries");
        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(entry => entry.LedgerTransactionId).HasColumnName("ledger_transaction_id");
        builder.Property(entry => entry.AccountId).HasColumnName("account_id");
        builder.Property(entry => entry.Type).HasColumnName("type").HasConversion<string>().IsRequired();
        builder.Property(entry => entry.CreatedAtUtc)
            .HasColumnName("created_at_utc").HasColumnType("timestamp with time zone");

        builder.ComplexProperty(entry => entry.Amount, money =>
        {
            money.IsRequired();
            money.Property(value => value.Amount).HasColumnName("amount").HasPrecision(18, 2).IsRequired();
            money.Ignore(value => value.Currency);
        });

        builder.HasOne<Account>().WithMany()
            .HasForeignKey(entry => entry.AccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(entry => entry.LedgerTransactionId);
        builder.HasIndex(entry => entry.AccountId);
    }
}
