using Bunit;
using Chess.Web.Api;
using Chess.Web.Components.Pages.Broadcast;
using Chess.Web.Components.Pages.Play;
using Chess.Web.Tests.Support;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Chess.Web.Tests.Broadcast;

/// <summary>Wires the OBS page to a <see cref="LiveBroadcastServer"/> and renders it with test-speed timings.</summary>
internal static class LivePageHarness
{
    public const string AfterE4 = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1";
    public const string AfterE5 = "rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq e6 0 2";

    public const string ReplayStatus = "Replay of a past game";

    public static void Wire(BunitContext ctx, LiveBroadcastServer server)
    {
        var client = Canned.Client(server.Handler);
        ctx.Services.AddSingleton(new ArenaApiClient(client));
        ctx.Services.AddSingleton(new GameEventsClient(client));
        ctx.Services.AddSingleton<IHttpClientFactory>(new ArenaClientFactory(client));
    }

    /// <summary>Renders the page polling every 20 ms. <paramref name="more"/> sets any further parameter.</summary>
    public static IRenderedComponent<LiveBroadcastPage> Render(BunitContext ctx,
        Action<ComponentParameterCollectionBuilder<LiveBroadcastPage>>? more = null)
    {
        ctx.Services.GetRequiredService<NavigationManager>().NavigateTo("http://localhost/broadcast");
        return ctx.Render<LiveBroadcastPage>(parameters =>
        {
            parameters.Add(page => page.PollInterval, TimeSpan.FromMilliseconds(20));
            parameters.Add(page => page.ReconnectDelay, TimeSpan.FromMilliseconds(10));
            more?.Invoke(parameters);
        });
    }

    public static string[] Highlighted(IRenderedComponent<LiveBroadcastPage> cut) =>
        cut.FindAll("[data-testid=broadcast-board] [data-highlight]")
            .Select(square => square.GetAttribute("data-highlight")!)
            .Order()
            .ToArray();

    public static bool GamesListRead(LiveBroadcastServer server) =>
        server.Handler.Requests.ToArray().Any(request => request.Target.StartsWith("/api/games?", StringComparison.Ordinal));

    /// <summary>How many times the page has read the request queue: one per poll.</summary>
    public static int QueueReads(LiveBroadcastServer server) =>
        server.Handler.Requests.ToArray().Count(request => request.Target == "/api/requests/queue");

    private sealed class ArenaClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            name == "ArenaApiClient" ? client : throw new InvalidOperationException($"No client named '{name}'.");
    }
}
