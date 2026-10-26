using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Chess.Llm;

public static class LlmServiceCollectionExtensions
{
    /// <summary>
    /// Registers the LLM gateway selected by <c>Llm:Provider</c>: <c>Copilot</c> (default) or <c>Fake</c>.
    /// Also registers the <c>copilot</c> health check, which reports the gateway state, and the <c>Arena</c> section
    /// (<c>Arena:CustomModels</c>), validated when the host starts.
    /// </summary>
    public static IHostApplicationBuilder AddLlmGateway(this IHostApplicationBuilder builder)
    {
        var provider = builder.Configuration["Llm:Provider"] ?? "Copilot";

        builder.Services.AddHealthChecks().AddCheck<LlmGatewayHealthCheck>("copilot");
        builder.Services.AddOptions<ArenaOptions>().BindConfiguration("Arena").ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<ArenaOptions>, ArenaOptionsValidator>();

        switch (provider.ToLowerInvariant())
        {
            case "copilot":
                builder.Services.AddOptions<CopilotOptions>().BindConfiguration("Copilot");
                builder.Services.AddSingleton<CopilotLlmGateway>();
                builder.Services.AddSingleton<ILlmGateway>(sp => sp.GetRequiredService<CopilotLlmGateway>());
                builder.Services.AddHostedService<CopilotClientHostedService>();
                break;

            case "fake":
                var seed = builder.Configuration.GetValue("Llm:Seed", 1);
                var latency = TimeSpan.FromMilliseconds(builder.Configuration.GetValue("Llm:FakeLatencyMs", 0));
                builder.Services.AddSingleton<ILlmGateway>(sp =>
                {
                    var arena = sp.GetRequiredService<IOptions<ArenaOptions>>().Value;
                    return new FakeLlmGateway(seed, latency, arena.CustomModels, arena.Cost.UsdPerAiCredit);
                });
                break;

            default:
                throw new InvalidOperationException($"Unknown Llm:Provider '{provider}'. Use 'Copilot' or 'Fake'.");
        }

        return builder;
    }
}
