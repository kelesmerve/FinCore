using FinCore.Application.Features.Transfers.Transfer;
using FinCore.Application.Features.Accounts.GetMyAccounts;
using FinCore.Application.Features.Users.Logout;
using FinCore.Application.Features.Users.Refresh;
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
        services.AddScoped<RefreshTokenHandler>();
        services.AddScoped<LogoutHandler>();
        services.AddScoped<TransferMoneyHandler>();
        services.AddScoped<IdempotentTransferHandler>();
        services.AddScoped<GetMyAccountsHandler>();
        return services;
    }
}
