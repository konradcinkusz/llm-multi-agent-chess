using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Chess.Llm;

/// <summary>The part of the Copilot gateway the host starts and stops. Internal, so the hosted service can be tested without a runtime.</summary>
internal interface ICopilotRuntime
{
    Task StartAsync(CancellationToken ct);

    Task StopAsync();
}

/// <summary>
/// Starts the Copilot runtime with the host and stops it on shutdown. Never fails the host (P8), and never holds it up: the runtime
/// starts in the background, like the migration, so the API listens while a runtime that hangs is still being waited for. Until the
/// start ends the gateway reports <see cref="LlmGatewayState.Unavailable"/> (degraded, never unhealthy), and it reports the outcome
/// as soon as it is known.
/// </summary>
internal sealed class CopilotClientHostedService(ICopilotRuntime runtime, ILogger<CopilotClientHostedService> logger) : IHostedService
{
    private readonly CancellationTokenSource _stopping = new();
    private Task _start = Task.CompletedTask;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _start = Task.Run(() => RunAsync(_stopping.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _stopping.CancelAsync();
        try
        {
            await _start.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // The host's shutdown budget ran out; the runtime is closed below all the same.
        }

        await runtime.StopAsync();
        _stopping.Dispose();
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            await runtime.StartAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The host is stopping before the runtime was up.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The Copilot runtime could not be started; LLM players are unavailable");
        }
    }
}
