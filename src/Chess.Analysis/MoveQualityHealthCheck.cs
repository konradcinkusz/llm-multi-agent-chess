using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Chess.Analysis;

/// <summary>
/// Reports the <c>stockfish</c> integration. Absent is still Healthy: move quality is just disabled. A configured binary
/// that does not complete the UCI handshake is Degraded, never Unhealthy: the arena plays on without move quality.
/// </summary>
internal sealed class MoveQualityHealthCheck(IMoveQualityAnalyzer analyzer) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (analyzer is StockfishAnalyzer stockfish)
        {
            try
            {
                await stockfish.ProbeAsync(cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                return HealthCheckResult.Degraded($"binary does not work — move quality disabled: {ex.Message}");
            }
        }

        return analyzer.IsAvailable
            ? HealthCheckResult.Healthy("available")
            : HealthCheckResult.Healthy("absent — move quality disabled");
    }
}
