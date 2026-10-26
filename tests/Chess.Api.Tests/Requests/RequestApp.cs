using Chess.Api.Persistence;
using Chess.Api.Requests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Chess.Api.Tests.Requests;

/// <summary>
/// The app on the InMemory provider with its own store, the fake gateway and the fake profile's request menu. Requests are not
/// bring-your-own-key here, as in the fake profile. The mode is a parameter, so the simulate endpoint can be tested off Mock too.
/// </summary>
internal sealed class RequestApp(string mode = "Mock") : WebApplicationFactory<Program>
{
    private readonly string databaseName = $"requests-{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Database:Provider", "InMemory");
        builder.UseSetting("Database:InMemoryName", databaseName);
        builder.UseSetting("Llm:Provider", "Fake");
        builder.UseSetting("Arena:Requests:Mode", mode);
        builder.UseSetting("Arena:Requests:RequireApiKeyModels", "false");
        builder.UseSetting("Arena:Requests:Menu:0:Code", "fa");
        builder.UseSetting("Arena:Requests:Menu:0:PlayerId", "fake-a");
        builder.UseSetting("Arena:Requests:Menu:1:Code", "fb");
        builder.UseSetting("Arena:Requests:Menu:1:PlayerId", "fake-b");
        builder.UseSetting("Arena:Requests:Menu:2:Code", "fah");
        builder.UseSetting("Arena:Requests:Menu:2:PlayerId", "fake-a@high");
    }

    public IDbContextFactory<ArenaDbContext> Factory => Services.GetRequiredService<IDbContextFactory<ArenaDbContext>>();

    public IChatIntake Intake => Services.GetRequiredService<IChatIntake>();

    public GameRequestQueue Queue => Services.GetRequiredService<GameRequestQueue>();

    public RequestMenu Menu => Services.GetRequiredService<RequestMenu>();
}

/// <summary>Builders for chat events in tests. Times step by a minute, so the queue's FIFO order is the order of the calls.</summary>
internal static class Chat
{
    private static readonly DateTimeOffset Start = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);
    private static int minute;

    /// <summary>A Super Chat: a paid message, with its amount in the currency's units.</summary>
    public static ChatEvent Paid(string externalId, string payerId, double units, string currency, string text) =>
        new("mock", externalId, payerId, payerId, Money.FromUnits(units), currency, text, Next());

    /// <summary>A plain chat message.</summary>
    public static ChatEvent Plain(string externalId, string payerId, string text) =>
        new("mock", externalId, payerId, payerId, null, null, text, Next());

    private static DateTimeOffset Next() => Start.AddMinutes(Interlocked.Increment(ref minute));
}
