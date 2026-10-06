using System.Text;
using FinCore.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using FinCore.Application;
using FinCore.Infrastructure;
using FinCore.Application.Features.Users.BootstrapAdmin;
using FinCore.Application.Features.Users.Register;
using FinCore.Api.Health;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var bootstrapAdmin = args.Contains("--bootstrap-admin", StringComparer.Ordinal);
var builder = WebApplication.CreateBuilder(args.Where(argument => argument != "--bootstrap-admin").ToArray());

var connectionString = builder.Configuration.GetConnectionString("FinCoreDatabase");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Connection string 'FinCoreDatabase' is missing or empty. Configure ConnectionStrings:FinCoreDatabase using User Secrets or environment variables.");
}

builder.Services.AddInfrastructure(connectionString, builder.Configuration.GetSection("Jwt"));
builder.Services.AddApplication();
builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks().AddCheck<PostgresReadinessHealthCheck>("postgresql", tags: ["ready"]);
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((options, jwtOptions) =>
    {
        var jwt = jwtOptions.Value;
        options.MapInboundClaims = false;
        options.IncludeErrorDetails = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SecretKey)),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ClockSkew = TimeSpan.Zero,
            RoleClaimType = "role",
            NameClaimType = "sub"
        };
    });
builder.Services.AddAuthorization(options =>
    options.AddPolicy("AdminOnly", policy =>
        policy.RequireAuthenticatedUser().RequireRole(FinCore.Application.Security.RoleNames.Admin)));

var app = builder.Build();

if (bootstrapAdmin)
{
    var email = app.Configuration["BootstrapAdmin:Email"];
    var password = app.Configuration["BootstrapAdmin:Password"];
    if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
    {
        Console.Error.WriteLine("BootstrapAdmin:Email and BootstrapAdmin:Password must be configured.");
        Environment.ExitCode = 1;
        return;
    }

    try
    {
        await using var scope = app.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<BootstrapAdminHandler>()
            .HandleAsync(email, password);
        Console.WriteLine(result == BootstrapAdminResult.Created
            ? "Admin bootstrap completed." : "Admin already exists; no changes made.");
    }
    catch (RegistrationValidationException)
    {
        Console.Error.WriteLine("Admin bootstrap email or password does not meet registration rules.");
        Environment.ExitCode = 1;
    }
    catch (AdminBootstrapConflictException)
    {
        Console.Error.WriteLine("Admin bootstrap conflict: email belongs to a customer.");
        Environment.ExitCode = 1;
    }
    catch (Exception)
    {
        Console.Error.WriteLine("Admin bootstrap failed.");
        Environment.ExitCode = 1;
    }
    return;
}

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = static (context, report) =>
        context.Response.WriteAsync(report.Status == HealthStatus.Healthy ? "Healthy" : "Unhealthy")
}).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
    ResponseWriter = static (context, report) =>
        context.Response.WriteAsync(report.Status == HealthStatus.Healthy ? "Healthy" : "Unhealthy")
}).AllowAnonymous();
app.MapControllers();

app.Run();

public partial class Program { }
