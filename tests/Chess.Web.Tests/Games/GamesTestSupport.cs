using System.Net;
using System.Text;
using System.Text.Json;
using Bunit;
using Chess.Web.Api;
using Chess.Web.Components.Pages.Games;
using Chess.Web.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace Chess.Web.Tests.Games;

internal static class Wire
{
    /// <summary>Registers the API client, answering from <paramref name="handler"/>.</summary>
    public static void Api(BunitContext ctx, FakeHandler handler) =>
        ctx.Services.AddSingleton(new ArenaApiClient(Canned.Client(handler)));
}

internal static class Routes
{
    /// <summary>Routes are "METHOD /path?query". An unknown route answers 404, so a call the test did not expect fails loudly.</summary>
    public static FakeHandler Answer(IReadOnlyDictionary<string, Func<HttpResponseMessage>> routes) => new(request =>
        routes.TryGetValue($"{request.Method} {request.Target}", out var answer)
            ? answer()
            : Canned.Json("{}", HttpStatusCode.NotFound));
}

internal static class Reply
{
    public static Func<HttpResponseMessage> Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        () => Canned.Json(json, status);

    public static Func<HttpResponseMessage> Pgn(string pgn) => () => new(HttpStatusCode.OK)
    {
        Content = new StringContent(pgn, Encoding.UTF8, "application/x-chess-pgn"),
    };
}

internal static class Fixtures
{
    public const string StartFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
    public const string AfterE4 = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1";
    public const string AfterE4E5 = "rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq e6 0 2";

    public const string FinishedId = "44444444-4444-4444-4444-444444444444";
    public const string RunningId = "55555555-5555-5555-5555-555555555555";

    public const string Pgn = "[Event \"Arena #1\"]\n[White \"gpt\"]\n\n1. e4 e5 2. Nf3 1-0\n";

    public const string Models = """
        [
          {"id":"gpt","displayName":"GPT","premiumMultiplier":1,"enabled":true},
          {"id":"claude","displayName":"Claude","premiumMultiplier":1.5,"enabled":true}
        ]
        """;

    public static object Summary(string id, string white, string black, string state = "finished", string result = "1-0",
        string? termination = "checkmate", int plies = 2) => new
        {
            id,
            tournamentId = (string?)null,
            white = new { kind = "llm", modelId = white },
            black = new { kind = "llm", modelId = black },
            rated = true,
            state,
            result,
            termination,
            plyCount = plies,
            createdAt = "2026-10-24T10:00:00Z",
            finishedAt = state == "finished" ? "2026-10-24T11:00:00Z" : null,
        };

    public static object Move(int ply, string color, string san, string fenAfter, int attempts = 1, long latencyMs = 500,
        int? centipawnLoss = null, string? bestSan = null) => new
        {
            ply,
            color,
            san,
            fenAfter,
            attempts,
            latencyMs,
            inputTokens = 120,
            outputTokens = 8,
            centipawnLoss,
            bestSan,
        };

    public static object Detail(object summary, string startFen, string currentFen, params object[] moves) => new
    {
        summary,
        startFen,
        currentFen,
        moves,
    };

    public static object Page(IEnumerable<object> items, int total, int page = 1) => new
    {
        items,
        page,
        pageSize = GameArchive.PageSize,
        total,
    };

    /// <summary>An attempt as GET /api/attempts returns it (03 §10.1). A legal attempt names its move; any other is rejected.</summary>
    public static object Attempt(string gameId, int ply, int number, string modelId, bool legal, string? rejectionKind = null,
        string? san = null, string? reasoning = null) => new
        {
            gameId,
            ply,
            number,
            modelId,
            prompt = $"Prompt for ply {ply}, attempt {number}",
            rawReply = legal ? san : "not a move",
            parsedSan = legal ? san : null,
            legal,
            latencyMs = 500L,
            error = (string?)null,
            createdAt = "2026-10-24T10:00:01Z",
            reasoning,
            inputTokens = 120,
            outputTokens = 8,
            reasoningTokens = (int?)null,
            aiCredits = (double?)null,
            rejectionKind,
        };

    /// <summary>The usage of a game (GET /api/games/{id}/usage): the white side pays <paramref name="usd"/>, the black side is unknown.</summary>
    public static object Usage(string id, double? usd) => new
    {
        gameId = id,
        white = new
        {
            color = "white",
            modelId = "gpt",
            requests = 2,
            inputTokens = 240L,
            outputTokens = 16L,
            reasoningTokens = 4L,
            cachedInputTokens = 0L,
            aiCredits = usd * 100,
            estimatedUsd = usd,
            costBasis = "reported",
        },
        black = new
        {
            color = "black",
            modelId = "claude",
            requests = 1,
            inputTokens = 120L,
            outputTokens = 8L,
            reasoningTokens = 0L,
            cachedInputTokens = 0L,
            aiCredits = (double?)null,
            estimatedUsd = (double?)null,
            costBasis = "unknown",
        },
        totalAiCredits = usd * 100,
        totalEstimatedUsd = usd,
        usdPerAiCredit = 0.01,
    };

    public static string Json(object value) => JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
}
