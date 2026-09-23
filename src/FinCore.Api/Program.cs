using FinCore.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("FinCoreDatabase");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Connection string 'FinCoreDatabase' is missing or empty. Configure ConnectionStrings:FinCoreDatabase using User Secrets or environment variables.");
}

builder.Services.AddInfrastructure(connectionString);

var app = builder.Build();

app.Run();
