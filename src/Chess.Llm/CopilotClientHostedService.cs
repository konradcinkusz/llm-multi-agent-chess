using Microsoft.Extensions.Hosting;

namespace Chess.Llm;

/// <summary>Starts the Copilot runtime with the host and stops it on shutdown. Never fails the host (P8).</summary>
internal sealed class CopilotClientHostedService(CopilotLlmGateway gateway) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => gateway.StartAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => gateway.StopAsync();
}
