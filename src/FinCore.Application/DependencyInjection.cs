using FinCore.Application.Features.Users.Register;
using Microsoft.Extensions.DependencyInjection;

namespace FinCore.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<RegisterUserHandler>();
        return services;
    }
}
