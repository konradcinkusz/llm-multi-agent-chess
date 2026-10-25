using Chess.Api.Contracts;
using Chess.Llm;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Chess.Api.Tests.Usage;

/// <summary>
/// The deterministic fake, with usage: every reply reports input ≈ prompt length / 4 and output ≈ reply length / 4 + 2
/// tokens. <c>fake-a</c> and <c>fake-b</c> list prices (1.0 / 4.0 / 0.1 credits per million tokens); <c>auto</c> lists none.
/// </summary>
internal sealed class PricedFakeGateway(FakeLlmGateway inner) : ILlmGateway
{
    public static readonly LlmTokenPrices FakePrices = new(1_000_000, 1.0, 4.0, 0.1);
    private static readonly string[] Efforts = ["low", "medium", "high"];

    public LlmGatewayStatus Status => inner.Status;

    public Task<IReadOnlyList<LlmModelInfo>> ListModelsAsync(CancellationToken ct)
    {
        IReadOnlyList<LlmModelInfo> models =
        [
            new LlmModelInfo("fake-a", "Fake A", 0, true, FakePrices, Efforts, "medium"),
            new LlmModelInfo("fake-b", "Fake B", 0, true, FakePrices, Efforts, "medium"),
            new LlmModelInfo("auto", "Auto", null, true),
        ];
        return Task.FromResult(models);
    }

    public async Task<ILlmConversation> StartConversationAsync(LlmConversationOptions options, CancellationToken ct) =>
        new UsageConversation(await inner.StartConversationAsync(options, ct));

    private sealed class UsageConversation(ILlmConversation inner) : ILlmConversation
    {
        public string ModelId => inner.ModelId;

        public async Task<LlmReply> AskAsync(string prompt, CancellationToken ct)
        {
            var reply = await inner.AskAsync(prompt, ct);
            return new LlmReply(reply.Text, reply.Latency, prompt.Length / 4, reply.Text.Length / 4 + 2);
        }

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}

/// <summary>
/// The app on the InMemory provider, with <see cref="PricedFakeGateway"/> and games capped at 30 plies. The owner's
/// settings are passed as configuration, so the test reads the same keys the app does.
/// </summary>
internal sealed class UsageApp(bool showAutoModel = false, double usdPerAiCredit = 0.01) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Database:Provider", "InMemory");
        builder.UseSetting("Database:InMemoryName", Guid.NewGuid().ToString());
        builder.UseSetting("Llm:Provider", "Fake");
        builder.UseSetting("Arena:MaxPlies", "30");
        builder.UseSetting("Arena:ShowAutoModel", showAutoModel ? "true" : "false");
        builder.UseSetting("Arena:Cost:UsdPerAiCredit", usdPerAiCredit.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ILlmGateway>();
            services.AddSingleton<ILlmGateway>(new PricedFakeGateway(new FakeLlmGateway(seed: 5)));
        });
    }
}
