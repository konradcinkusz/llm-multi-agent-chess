using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Chess.Api.Persistence;

/// <summary>Reports the startup migration: Degraded while migrating, Healthy after, Unhealthy on failure.</summary>
public sealed class DatabaseHealthCheck(DatabaseReadiness readiness) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var status = readiness.Current;
        HealthCheckResult result = status.State switch
        {
            DatabaseState.Migrating => HealthCheckResult.Degraded("migrating"),
            DatabaseState.Ready => HealthCheckResult.Healthy("ready"),
            _ => HealthCheckResult.Unhealthy(status.Error ?? "migration failed"),
        };
        return Task.FromResult(result);
    }
}
