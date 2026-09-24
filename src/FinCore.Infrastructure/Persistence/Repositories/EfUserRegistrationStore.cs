using FinCore.Application.Abstractions.Persistence;
using FinCore.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinCore.Infrastructure.Persistence.Repositories;

public sealed class EfUserRegistrationStore : IUserRegistrationStore
{
    private readonly FinCoreDbContext _context;

    public EfUserRegistrationStore(FinCoreDbContext context)
    {
        _context = context;
    }

    public Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken cancellationToken)
        => _context.Users.AsNoTracking().AnyAsync(user => user.Email == normalizedEmail, cancellationToken);

    public async Task AddAsync(User user, Account account, CancellationToken cancellationToken)
    {
        await _context.Users.AddAsync(user, cancellationToken);
        await _context.Accounts.AddAsync(account, cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        await _context.SaveChangesAsync(cancellationToken);
    }
}
