using FinCore.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FinCore.Api.Health;

public sealed class PostgresReadinessHealthCheck(FinCoreDbContext context) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext healthCheckContext, CancellationToken cancellationToken = default)
    {
        try
        {
            return await context.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return HealthCheckResult.Unhealthy();
        }
    }
}
