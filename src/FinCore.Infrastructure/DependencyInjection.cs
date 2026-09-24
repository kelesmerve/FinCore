using FinCore.Application.Abstractions.Persistence;
using FinCore.Application.Abstractions.Security;
using FinCore.Infrastructure.Accounts;
using FinCore.Infrastructure.Persistence.Repositories;
using FinCore.Application.Security;
using FinCore.Infrastructure.Security;
using FinCore.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinCore.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<FinCoreDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IPasswordHasher, AspNetCorePasswordHasher>();
        services.AddScoped<IUserRegistrationStore, EfUserRegistrationStore>();
        services.AddSingleton<IAccountNumberGenerator, GuidAccountNumberGenerator>();
        return services;
    }
}
