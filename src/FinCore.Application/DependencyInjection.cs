using FinCore.Application.Features.Users.ListUsers;
using FinCore.Application.Features.Users.Login;
using FinCore.Application.Features.Users.Register;
using Microsoft.Extensions.DependencyInjection;

namespace FinCore.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<RegisterUserHandler>();
        services.AddScoped<LoginUserHandler>();
        services.AddScoped<ListUsersHandler>();
        return services;
    }
}
