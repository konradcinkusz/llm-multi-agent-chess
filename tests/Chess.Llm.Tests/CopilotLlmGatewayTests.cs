using Chess.Llm;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Chess.Llm.Tests;

/// <summary>
/// Only the degraded paths are tested: no Copilot CLI or sign-in exists in CI, so every runtime start
/// is pointed at something that cannot answer.
/// </summary>
public class CopilotLlmGatewayTests
{
    private const string MissingCli = "/nonexistent/chess-test-copilot-cli";

    [Fact]
    public async Task InvalidCliPathReportsUnavailableInsteadOfThrowing()
    {
        var gateway = NewGateway(new CopilotOptions { CliPath = MissingCli });

        await gateway.StartAsync(CancellationToken.None);

        Assert.Equal(LlmGatewayState.Unavailable, gateway.Status.State);
        Assert.StartsWith("Copilot runtime unavailable", gateway.Status.Detail);
    }

    [Fact]
    public async Task InvalidUriReportsUnavailableInsteadOfThrowing()
    {
        // Port 1 on loopback refuses connections, so the URI branch fails fast without leaving the machine.
        var gateway = NewGateway(new CopilotOptions { CliUrl = "127.0.0.1:1", StartupTimeout = TimeSpan.FromSeconds(10) });

        await gateway.StartAsync(CancellationToken.None);

        Assert.Equal(LlmGatewayState.Unavailable, gateway.Status.State);
    }

    [Fact]
    public async Task UnavailableGatewayRefusesModelsAndConversations()
    {
        var gateway = NewGateway(new CopilotOptions { CliPath = MissingCli });
        await gateway.StartAsync(CancellationToken.None);

        await Assert.ThrowsAsync<LlmUnavailableException>(() => gateway.ListModelsAsync(CancellationToken.None));
        await Assert.ThrowsAsync<LlmUnavailableException>(() => gateway.StartConversationAsync(
            new LlmConversationOptions("gpt-x", "system", TimeSpan.FromSeconds(5)), CancellationToken.None));
    }

    [Fact]
    public async Task StopBeforeAnyStartIsHarmless()
    {
        var gateway = NewGateway(new CopilotOptions());

        await gateway.StopAsync();

        Assert.Equal(LlmGatewayState.Unavailable, gateway.Status.State);
    }

    [Fact]
    public async Task HostWithMissingCopilotStartsAndStopsWithoutFailing()
    {
        using var host = HostWithProvider(provider: null, copilotCliPath: MissingCli).Build();

        await host.StartAsync();

        var gateway = host.Services.GetRequiredService<ILlmGateway>();
        Assert.Equal(LlmGatewayState.Unavailable, gateway.Status.State);

        await host.StopAsync();
    }

    [Fact]
    public void CopilotIsTheDefaultProvider()
    {
        using var host = HostWithProvider(provider: null, copilotCliPath: MissingCli).Build();

        Assert.IsType<CopilotLlmGateway>(host.Services.GetRequiredService<ILlmGateway>());
    }

    [Fact]
    public void FakeProviderIsSelectedByConfiguration()
    {
        using var host = HostWithProvider(provider: "Fake", copilotCliPath: null).Build();

        Assert.IsType<FakeLlmGateway>(host.Services.GetRequiredService<ILlmGateway>());
    }

    [Fact]
    public void UnknownProviderFailsAtStartupConfiguration()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["Llm:Provider"] = "Openai";

        Assert.Throws<InvalidOperationException>(() => builder.AddLlmGateway());
    }

    private static CopilotLlmGateway NewGateway(CopilotOptions options) =>
        new(Options.Create(options), NullLogger<CopilotLlmGateway>.Instance);

    private static HostApplicationBuilder HostWithProvider(string? provider, string? copilotCliPath)
    {
        var builder = Host.CreateApplicationBuilder();
        if (provider is not null)
        {
            builder.Configuration["Llm:Provider"] = provider;
        }

        if (copilotCliPath is not null)
        {
            builder.Configuration["Copilot:CliPath"] = copilotCliPath;
        }

        builder.AddLlmGateway();
        return builder;
    }
}
