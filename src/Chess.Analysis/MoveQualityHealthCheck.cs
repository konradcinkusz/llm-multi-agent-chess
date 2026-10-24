using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Chess.Analysis;

/// <summary>Reports the <c>stockfish</c> integration. Absent is still Healthy: move quality is just disabled.</summary>
internal sealed class MoveQualityHealthCheck(IMoveQualityAnalyzer analyzer) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        => Task.FromResult(analyzer.IsAvailable
            ? HealthCheckResult.Healthy("available")
            : HealthCheckResult.Healthy("absent — move quality disabled"));
}
