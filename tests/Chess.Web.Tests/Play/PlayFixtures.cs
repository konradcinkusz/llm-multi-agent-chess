using System.IO.Pipelines;
using System.Net;
using System.Text;
using Chess.Web.Components.Pages.Play;
using Chess.Web.Tests.Support;

namespace Chess.Web.Tests.Play;

/// <summary>
/// A fake Chess.Api for the live page. REST calls are answered from the fields the test sets. The SSE
/// stream stays open through a pipe: the test sends events one at a time and asserts between them.
/// </summary>
internal sealed class PlayServer
{
    public const string GameId = "44444444-4444-4444-4444-444444444444";
    public const string GameTarget = "/api/games/" + GameId;

    private Pipe? _pipe;

    public PlayServer()
    {
        Handler = new FakeHandler(Respond);
    }

    public FakeHandler Handler { get; }

    public string Status { get; set; } = PlayFixtures.Ready;

    public string Models { get; set; } = PlayFixtures.Models;

    public HttpStatusCode CreateStatus { get; set; } = HttpStatusCode.Created;

    /// <summary>What GET /api/games/{id} answers. The page loads it right after it opens the stream.</summary>
    public string GameDetail { get; set; } = PlayFixtures.Detail("queued", "*", null);

    public bool StreamOpen => _pipe is not null;

    public async Task SendAsync(string eventType, string json)
    {
        var bytes = Encoding.UTF8.GetBytes($"event: {eventType}\ndata: {json}\n\n");
        await _pipe!.Writer.WriteAsync(bytes);
    }

    /// <summary>Closes the stream, as the server does after GameFinished or when the connection drops.</summary>
    public void EndStream() => _pipe?.Writer.Complete();

    private HttpResponseMessage Respond(RecordedRequest request)
    {
        var route = $"{request.Method} {request.Target}";
        if (route == "GET /api/models")
        {
            return Canned.Json(Models);
        }

        if (route == "GET /api/status")
        {
            return Canned.Json(Status);
        }

        if (route == "POST /api/games")
        {
            return Canned.Json(PlayFixtures.Summary("queued", "*", null, 0), CreateStatus);
        }

        if (route == $"GET {GameTarget}")
        {
            return Canned.Json(GameDetail);
        }

        if (route == $"GET {GameTarget}/events")
        {
            _pipe = new Pipe();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(_pipe.Reader.AsStream()) };
        }

        if (route == $"DELETE {GameTarget}")
        {
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }

        if (route == $"GET {GameTarget}/pgn")
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[Event \"Arena\"]\n\n1. e4 e5 1-0", Encoding.UTF8, "application/x-chess-pgn"),
            };
        }

        return Canned.Json("{}", HttpStatusCode.NotFound);
    }
}

internal static class PlayFixtures
{
    public const string AfterE4 = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1";
    public const string AfterE5 = "rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq e6 0 2";

    public const string Ready = """{"copilot":"Ready","copilotDetail":"signed in","database":"ok","stockfish":"absent"}""";

    public const string NotReady =
        """{"copilot":"NotAuthenticated","copilotDetail":"not logged in","database":"ok","stockfish":"absent"}""";

    public const string Models = """
        [
          {"id":"gpt","displayName":"GPT","premiumMultiplier":null,"enabled":true,"pricing":{"inputUsdPerMillionTokens":3,"outputUsdPerMillionTokens":15,"cachedInputUsdPerMillionTokens":0.3}},
          {"id":"claude","displayName":"Claude","premiumMultiplier":null,"enabled":true,"pricing":null},
          {"id":"random","displayName":"Random","premiumMultiplier":null,"enabled":true},
          {"id":"retired","displayName":"Retired","premiumMultiplier":1,"enabled":false}
        ]
        """;

    public static string Summary(string state, string result, string? termination, int plies) =>
        $$"""{"id":"{{PlayServer.GameId}}","tournamentId":null,"white":{"kind":"llm","modelId":"gpt"},"black":{"kind":"llm","modelId":"claude"},"rated":true,"state":"{{state}}","result":"{{result}}","termination":{{(termination is null ? "null" : $"\"{termination}\"")}},"plyCount":{{plies}},"createdAt":"2026-10-24T10:00:00Z","finishedAt":null}""";

    /// <summary>A game as GET /api/games/{id} reports it. Each move is (colour, SAN, FEN after it); ply N is move N.</summary>
    public static string Detail(string state, string result, string? termination,
        params (string Color, string San, string Fen)[] moves)
    {
        var plies = string.Join(',', moves.Select((move, index) => PlyJson(index + 1, move)));
        var current = moves.Length == 0 ? LiveGame.StartFen : moves[^1].Fen;
        return $$"""{"summary":{{Summary(state, result, termination, moves.Length)}},"startFen":"{{LiveGame.StartFen}}","currentFen":"{{current}}","moves":[{{plies}}]}""";
    }

    public static string MoveStarted(int ply, string color, string modelId) =>
        $$"""{"gameId":"{{PlayServer.GameId}}","at":"2026-10-24T10:00:00Z","ply":{{ply}},"color":"{{color}}","modelId":"{{modelId}}"}""";

    public static string AttemptRejected(int ply, int attempt, string rawReply, string reason) =>
        $$"""{"gameId":"{{PlayServer.GameId}}","at":"2026-10-24T10:00:01Z","ply":{{ply}},"attempt":{{attempt}},"rawReply":"{{rawReply}}","reason":"{{reason}}"}""";

    public static string MovePlayed(int ply, string color, string san, string fen) =>
        $$"""{"gameId":"{{PlayServer.GameId}}","at":"2026-10-24T10:00:02Z","ply":{{ply}},"color":"{{color}}","san":"{{san}}","fen":"{{fen}}","attempts":1,"latencyMs":1200}""";

    /// <summary>An attempt the engine stored (03 §10.1). A legal attempt has no rejection kind.</summary>
    public static string AttemptRecorded(int ply, int attempt, string color, string modelId, string san, bool legal,
        string? rejectionKind, string rawReply, string? reasoning = null, double? aiCredits = null) =>
        $$"""{"gameId":"{{PlayServer.GameId}}","at":"2026-10-24T10:00:01Z","ply":{{ply}},"attempt":{{attempt}},"color":"{{color}}","modelId":"{{modelId}}","prompt":"Play {{san}}","rawReply":"{{rawReply}}","reasoning":{{(reasoning is null ? "null" : $"\"{reasoning}\"")}},"parsedSan":{{(legal ? $"\"{san}\"" : "null")}},"legal":{{(legal ? "true" : "false")}},"latencyMs":900,"inputTokens":120,"outputTokens":30,"reasoningTokens":8,"aiCredits":{{(aiCredits is null ? "null" : aiCredits.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))}},"rejectionKind":{{(rejectionKind is null ? "null" : $"\"{rejectionKind}\"")}}}""";

    public static string GameFinished(string result, string termination) =>
        $$"""{"gameId":"{{PlayServer.GameId}}","at":"2026-10-24T10:05:00Z","result":"{{result}}","termination":"{{termination}}"}""";

    private static string PlyJson(int ply, (string Color, string San, string Fen) move) =>
        $$"""{"ply":{{ply}},"color":"{{move.Color}}","san":"{{move.San}}","fenAfter":"{{move.Fen}}","attempts":1,"latencyMs":1200,"inputTokens":null,"outputTokens":null,"centipawnLoss":null,"bestSan":null}""";
}
