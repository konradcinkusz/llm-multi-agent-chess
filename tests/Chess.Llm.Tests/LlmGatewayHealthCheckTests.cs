using Chess.Llm;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace Chess.Llm.Tests;

public class LlmGatewayHealthCheckTests
{
    [Theory]
    [InlineData(LlmGatewayState.Ready, HealthStatus.Healthy)]
    [InlineData(LlmGatewayState.Fake, HealthStatus.Healthy)]
    [InlineData(LlmGatewayState.NotAuthenticated, HealthStatus.Degraded)]
    [InlineData(LlmGatewayState.Unavailable, HealthStatus.Degraded)]
    public async Task MapsGatewayStateToHealthWithoutEverBeingUnhealthy(LlmGatewayState state, HealthStatus expected)
    {
        var gateway = new StubGateway(new LlmGatewayStatus(state, "detail for " + state));

        var result = await new LlmGatewayHealthCheck(gateway).CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(expected, result.Status);
        Assert.Equal("detail for " + state, result.Description);
    }

    private sealed class StubGateway(LlmGatewayStatus status) : ILlmGateway
    {
        public LlmGatewayStatus Status { get; } = status;

        public Task<IReadOnlyList<LlmModelInfo>> ListModelsAsync(CancellationToken ct) => throw new NotSupportedException();

        public Task<ILlmConversation> StartConversationAsync(LlmConversationOptions options, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
