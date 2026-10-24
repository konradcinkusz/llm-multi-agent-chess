using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Chess.Llm;

/// <summary>
/// Reports the gateway state as health. A missing sign-in or runtime is Degraded, never Unhealthy:
/// the rest of the app keeps working without a model (P8).
/// </summary>
public sealed class LlmGatewayHealthCheck(ILlmGateway gateway) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var status = gateway.Status;
        var result = status.State switch
        {
            LlmGatewayState.Ready or LlmGatewayState.Fake => HealthCheckResult.Healthy(status.Detail),
            _ => HealthCheckResult.Degraded(status.Detail),
        };

        return Task.FromResult(result);
    }
}
