using Chess.Api.Broadcast;
using Chess.Api.Persistence;
using Chess.Llm;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Chess.Api.Tests.Broadcast;

/// <summary>
/// The app on the InMemory provider with a named store, so that a test can seed schedules directly. Its gateway lists
/// premium multipliers (fake-a 3, fake-b unknown, fake-off disabled) that the fake gateway does not have.
/// </summary>
internal sealed class BroadcastApp(string databaseName) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Database:Provider", "InMemory");
        builder.UseSetting("Database:InMemoryName", databaseName);
        builder.UseSetting("Llm:Provider", "Fake");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ILlmGateway>();
            services.AddSingleton<ILlmGateway>(new PricedGateway());
        });
    }

    public ArenaDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ArenaDbContext>().UseInMemoryDatabase(databaseName).Options);
}

/// <summary>A gateway that answers the model list only; the broadcast endpoints never start a conversation.</summary>
internal sealed class PricedGateway : ILlmGateway
{
    public LlmGatewayStatus Status { get; } = new(LlmGatewayState.Fake, "Test gateway with premium multipliers");

    public Task<IReadOnlyList<LlmModelInfo>> ListModelsAsync(CancellationToken ct)
    {
        IReadOnlyList<LlmModelInfo> models =
        [
            new LlmModelInfo("fake-a", "Fake A", 3, true),
            new LlmModelInfo("fake-b", "Fake B", null, true),
            new LlmModelInfo("fake-off", "Fake Off", 1, false),
        ];
        return Task.FromResult(models);
    }

    public Task<ILlmConversation> StartConversationAsync(LlmConversationOptions options, CancellationToken ct) =>
        throw new NotSupportedException("The broadcast endpoints do not talk to models.");
}

/// <summary>One app with its own store and client, so that each test starts from the database it names.</summary>
internal sealed class BroadcastHarness : IDisposable
{
    public BroadcastApp App { get; } = new(Guid.NewGuid().ToString());

    public HttpClient Client { get; }

    public BroadcastHarness()
    {
        Client = App.CreateClient();
    }

    public void Dispose()
    {
        Client.Dispose();
        App.Dispose();
    }
}
