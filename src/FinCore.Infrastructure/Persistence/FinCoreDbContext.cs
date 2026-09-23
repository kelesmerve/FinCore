using FinCore.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinCore.Infrastructure.Persistence;

public sealed class FinCoreDbContext : DbContext
{
    public FinCoreDbContext(DbContextOptions<FinCoreDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<LedgerTransaction> LedgerTransactions => Set<LedgerTransaction>();
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FinCoreDbContext).Assembly);
    }
}
