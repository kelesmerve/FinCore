using System.Text;
using Microsoft.Extensions.Configuration;
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
        string connectionString, IConfiguration jwtConfiguration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<FinCoreDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IPasswordHasher, AspNetCorePasswordHasher>();
        services.AddScoped<IUserRegistrationStore, EfUserRegistrationStore>();
        services.AddSingleton<IAccountNumberGenerator, GuidAccountNumberGenerator>();
        services.AddOptions<JwtOptions>()
            .Bind(jwtConfiguration)
            .Validate(options => !string.IsNullOrWhiteSpace(options.Issuer), "Jwt:Issuer is required.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.Audience), "Jwt:Audience is required.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.SecretKey) &&
                Encoding.UTF8.GetByteCount(options.SecretKey) >= 32,
                "Jwt:SecretKey must contain at least 32 UTF-8 bytes.")
            .Validate(options => options.AccessTokenExpirationMinutes > 0,
                "Jwt:AccessTokenExpirationMinutes must be positive.")
            .ValidateOnStart();
        services.AddScoped<IUserAuthenticationStore, EfUserAuthenticationStore>();
        services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddScoped<IUserQueryStore, EfUserQueryStore>();
        return services;
    }
}
