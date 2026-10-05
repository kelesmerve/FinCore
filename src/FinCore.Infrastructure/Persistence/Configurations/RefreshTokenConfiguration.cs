using FinCore.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinCore.Infrastructure.Persistence.Configurations;

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");
        builder.HasKey(token => token.Id);
        builder.Property(token => token.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(token => token.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(token => token.FamilyId).HasColumnName("family_id").IsRequired();
        builder.Property(token => token.ParentTokenId).HasColumnName("parent_token_id").IsRequired(false);
        builder.Property(token => token.ReplacedByTokenId).HasColumnName("replaced_by_token_id").IsRequired(false);
        builder.Property(token => token.TokenHash).HasColumnName("token_hash").HasMaxLength(64).IsRequired();
        builder.Property(token => token.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("timestamp with time zone");
        builder.Property(token => token.ExpiresAtUtc).HasColumnName("expires_at_utc").HasColumnType("timestamp with time zone");
        builder.Property(token => token.RevokedAtUtc).HasColumnName("revoked_at_utc").HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property<uint>("xmin").IsRowVersion();

        builder.HasOne<User>().WithMany().HasForeignKey(token => token.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RefreshToken>().WithMany().HasForeignKey(token => token.ParentTokenId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RefreshToken>().WithMany().HasForeignKey(token => token.ReplacedByTokenId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(token => token.TokenHash).IsUnique();
        builder.HasIndex(token => token.FamilyId);
        builder.HasIndex(token => token.UserId);
        builder.HasIndex(token => token.ParentTokenId);
        builder.HasIndex(token => token.ReplacedByTokenId);
    }
}
