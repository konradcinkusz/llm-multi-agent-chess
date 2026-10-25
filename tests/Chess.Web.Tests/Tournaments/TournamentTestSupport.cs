using System.Net;
using Bunit;
using Chess.Web.Api;
using Chess.Web.Components.Pages.Results;
using Chess.Web.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace Chess.Web.Tests.Tournaments;

internal sealed record Reply(HttpStatusCode Status, string Json);

internal static class Serve
{
    /// <summary>Routes are "METHOD /path?query". An unknown route answers 404, so a call the test did not expect fails loudly.</summary>
    public static FakeHandler Routes(IReadOnlyDictionary<string, Reply> routes) => new(request =>
        routes.TryGetValue($"{request.Method} {request.Target}", out var reply)
            ? Canned.Json(reply.Json, reply.Status)
            : Canned.Json("{}", HttpStatusCode.NotFound));

    public static Reply Ok(string json) => new(HttpStatusCode.OK, json);

    public static Reply Created(string json) => new(HttpStatusCode.Created, json);

    public static Reply Fail(HttpStatusCode status) => new(status, "{}");
}

internal static class Wire
{
    /// <summary>Registers the API client and the named client the Results adapter uses, both answering from <paramref name="handler"/>.</summary>
    public static void Api(BunitContext ctx, FakeHandler handler)
    {
        ctx.Services.AddSingleton(new ArenaApiClient(Canned.Client(handler)));
        ctx.Services.AddHttpClient(TournamentRatingsClient.ClientName, client => client.BaseAddress = new Uri("http://api"))
            .ConfigurePrimaryHttpMessageHandler(() => handler);
    }
}

internal static class Fixtures
{
    public const string Models = """
        [
          {"id":"gpt","displayName":"GPT","premiumMultiplier":1,"enabled":true,"pricing":{"inputUsdPerMillionTokens":3,"outputUsdPerMillionTokens":15,"cachedInputUsdPerMillionTokens":0.3}},
          {"id":"claude","displayName":"Claude","premiumMultiplier":1.5,"enabled":true},
          {"id":"random","displayName":"Random","premiumMultiplier":null,"enabled":true},
          {"id":"retired","displayName":"Retired","premiumMultiplier":1,"enabled":false}
        ]
        """;

    public const string RunningId = "11111111-1111-1111-1111-111111111111";
    public const string FinishedId = "22222222-2222-2222-2222-222222222222";

    public const string RunningTournament =
        """{"id":"11111111-1111-1111-1111-111111111111","name":"Spring cup","models":["gpt","claude"],"gamesPerPair":2,"status":"running","gamesTotal":2,"gamesFinished":1,"createdAt":"2026-10-24T10:00:00Z","finishedAt":null}""";

    public const string FinishedTournament =
        """{"id":"22222222-2222-2222-2222-222222222222","name":"Summer cup","models":["gpt","claude"],"gamesPerPair":2,"status":"finished","gamesTotal":2,"gamesFinished":2,"createdAt":"2026-09-01T10:00:00Z","finishedAt":"2026-09-02T10:00:00Z"}""";

    public static string Tournaments(params string[] items) => $"[{string.Join(',', items)}]";

    public static string Page(IEnumerable<string> items, int total) =>
        $$"""{"items":[{{string.Join(',', items)}}],"page":1,"pageSize":200,"total":{{total}}}""";

    /// <summary>A game of the archive. <paramref name="tournamentId"/> is null for a game outside any tournament.</summary>
    public static string Game(string id, string? tournamentId, string white, string black, string result, int plies, string createdAt) =>
        $$"""{"id":"{{id}}","tournamentId":{{(tournamentId is null ? "null" : $"\"{tournamentId}\"")}},"white":{"kind":"llm","modelId":"{{white}}"},"black":{"kind":"llm","modelId":"{{black}}"},"rated":true,"state":"finished","result":"{{result}}","termination":"checkmate","plyCount":{{plies}},"createdAt":"{{createdAt}}","finishedAt":"2026-10-24T11:00:00Z"}""";
}
