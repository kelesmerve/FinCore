using FinCore.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinCore.Infrastructure.Persistence.Configurations;

public sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("accounts");
        builder.HasKey(account => account.Id);
        builder.Property(account => account.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(account => account.UserId).HasColumnName("user_id");
        builder.Property(account => account.AccountNumber)
            .HasColumnName("account_number").HasMaxLength(34).IsRequired();
        builder.HasIndex(account => account.AccountNumber).IsUnique();
        builder.Property(account => account.Status)
            .HasColumnName("status").HasConversion<string>().IsRequired();
        builder.Property(account => account.CreatedAtUtc)
            .HasColumnName("created_at_utc").HasColumnType("timestamp with time zone");

        builder.ComplexProperty(account => account.Balance, money =>
        {
            money.IsRequired();
            money.Property(value => value.Amount).HasColumnName("balance").HasPrecision(18, 2).IsRequired();
            money.Ignore(value => value.Currency);
        });
    }
}
