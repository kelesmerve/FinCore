using FinCore.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinCore.Infrastructure.Persistence.Configurations;

public sealed class LedgerTransactionConfiguration : IEntityTypeConfiguration<LedgerTransaction>
{
    public void Configure(EntityTypeBuilder<LedgerTransaction> builder)
    {
        builder.ToTable("ledger_transactions");
        builder.HasKey(transaction => transaction.Id);
        builder.Property(transaction => transaction.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(transaction => transaction.SourceAccountId).HasColumnName("source_account_id");
        builder.Property(transaction => transaction.DestinationAccountId).HasColumnName("destination_account_id");
        builder.Property(transaction => transaction.CreatedAtUtc)
            .HasColumnName("created_at_utc").HasColumnType("timestamp with time zone");

        builder.ComplexProperty(transaction => transaction.Amount, money =>
        {
            money.IsRequired();
            money.Property(value => value.Amount).HasColumnName("amount").HasPrecision(18, 2).IsRequired();
            money.Ignore(value => value.Currency);
        });

        builder.HasOne<Account>().WithMany()
            .HasForeignKey(transaction => transaction.SourceAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Account>().WithMany()
            .HasForeignKey(transaction => transaction.DestinationAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(transaction => transaction.SourceAccountId);
        builder.HasIndex(transaction => transaction.DestinationAccountId);

        builder.HasMany(transaction => transaction.Entries).WithOne()
            .HasForeignKey(entry => entry.LedgerTransactionId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(transaction => transaction.Entries)
            .HasField("_entries").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
