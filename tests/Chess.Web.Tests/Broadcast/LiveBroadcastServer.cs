using System.IO.Pipelines;
using System.Net;
using System.Text;
using Chess.Web.Components.Pages.Play;
using Chess.Web.Tests.Support;

namespace Chess.Web.Tests.Broadcast;

/// <summary>
/// A fake Chess.Api for the OBS page. REST answers come from the fields the test sets. The SSE stream stays open
/// through a pipe, so the test sends events one at a time and asserts between them.
/// </summary>
internal sealed class LiveBroadcastServer
{
    public static readonly Guid GameId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private const string GameTarget = "/api/games/55555555-5555-5555-5555-555555555555";

    private Pipe? _pipe;
    private int _streamRequests;
    private int _ratingsRequests;

    public LiveBroadcastServer()
    {
        Handler = new FakeHandler(Respond);
    }

    public FakeHandler Handler { get; }

    public HttpStatusCode NowStatus { get; set; } = HttpStatusCode.OK;

    public string Now { get; set; } = LiveFixtures.Now("idle", "null", "[]", "null");

    public string Schedules { get; set; } = LiveFixtures.Schedules("running", 48, 16);

    public string Leaderboard { get; set; } = LiveFixtures.Leaderboard(LiveFixtures.Ratings);

    public string Models { get; set; } = LiveFixtures.Models;

    /// <summary>What GET /api/games/{id} answers. The page loads it right after it opens the stream.</summary>
    public string GameDetail { get; set; } = LiveFixtures.Detail("running", "*", null);

    /// <summary>What GET /api/requests/queue answers. Null (the default) is an API without requests: a 404.</summary>
    public string? Queue { get; set; }

    /// <summary>When true, GET /api/requests/queue answers 500: an API that is up but failing.</summary>
    public bool QueueFails { get; set; }

    /// <summary>What GET /api/requests/menu answers. Null is a 404.</summary>
    public string? Menu { get; set; }

    /// <summary>What GET /api/games answers, for the replays. Null is a 404.</summary>
    public string? Games { get; set; }

    /// <summary>How many times the page has opened the event stream.</summary>
    public int StreamRequests => Volatile.Read(ref _streamRequests);

    /// <summary>How many times the page has read the leaderboard.</summary>
    public int RatingsRequests => Volatile.Read(ref _ratingsRequests);

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
        if (route == "GET /api/broadcast/now")
        {
            return NowStatus == HttpStatusCode.OK ? Canned.Json(Now) : Canned.Json("{}", NowStatus);
        }

        if (route == "GET /api/broadcast/schedules")
        {
            return Canned.Json(Schedules);
        }

        if (route == "GET /api/ratings")
        {
            Interlocked.Increment(ref _ratingsRequests);
            return Canned.Json(Leaderboard);
        }

        if (route == "GET /api/models")
        {
            return Canned.Json(Models);
        }

        if (route == "GET /api/requests/queue")
        {
            if (QueueFails)
            {
                return Canned.Json("{}", HttpStatusCode.InternalServerError);
            }

            return Queue is null ? Canned.Json("{}", HttpStatusCode.NotFound) : Canned.Json(Queue);
        }

        if (route == "GET /api/requests/menu")
        {
            return Menu is null ? Canned.Json("{}", HttpStatusCode.NotFound) : Canned.Json(Menu);
        }

        if (request.Method == "GET" && request.Target.StartsWith("/api/games?", StringComparison.Ordinal))
        {
            return Games is null ? Canned.Json("{}", HttpStatusCode.NotFound) : Canned.Json(Games);
        }

        if (route == $"GET {GameTarget}")
        {
            return Canned.Json(GameDetail);
        }

        if (route == $"GET {GameTarget}/events")
        {
            // The pipe is in place before the count moves, so a test that waits for the count can send at once.
            _pipe = new Pipe();
            Interlocked.Increment(ref _streamRequests);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(_pipe.Reader.AsStream()) };
        }

        return Canned.Json("{}", HttpStatusCode.NotFound);
    }
}

internal static class LiveFixtures
{
    public const string Models = """
        [
          {"id":"gpt","displayName":"GPT","premiumMultiplier":1,"enabled":true},
          {"id":"claude","displayName":"Claude","premiumMultiplier":1.5,"enabled":true},
          {"id":"gemini","displayName":"Gemini","premiumMultiplier":1,"enabled":true},
          {"id":"random","displayName":"Random","premiumMultiplier":null,"enabled":true}
        ]
        """;

    /// <summary>Ten models, so the default and ?top= cuts are both visible. Arena score falls with the index.</summary>
    public static readonly IReadOnlyList<(string Id, string Name)> Ratings =
    [
        ("m01", "Model 01"), ("m02", "Model 02"), ("m03", "Model 03"), ("m04", "Model 04"), ("m05", "Model 05"),
        ("m06", "Model 06"), ("m07", "Model 07"), ("m08", "Model 08"), ("m09", "Model 09"), ("m10", "Model 10"),
    ];

    public static string Leaderboard(IReadOnlyList<(string Id, string Name)> models)
    {
        var rows = models.Select((model, index) =>
            $$"""{"model":"{{model.Id}}","rating":{{1600 - index * 20}},"ciLow":1500,"ciHigh":1700,"games":12,"score":0.5,"legality":1,"moveQuality":null,"quality":0.5,"arenaScore":{{(0.9 - index * 0.05).ToString(System.Globalization.CultureInfo.InvariantCulture)}},"provisional":false}""");
        return $$"""{"models":[{{string.Join(',', rows)}}],"whiteAdvantage":0.1,"games":30,"moveQualityAvailable":false}""";
    }

    public static string Now(string status, string current, string next, string notice) =>
        $$"""{"status":"{{status}}","current":{{current}},"next":{{next}},"notice":{{notice}},"endsAt":"2026-10-25T10:00:00Z"}""";

    public static string Slot(int index, string white, string black, string state, Guid? gameId = null, string start = "2026-10-24T18:00:00Z") =>
        $$"""{"id":"{{Guid.NewGuid()}}","index":{{index}},"white":{"kind":"llm","modelId":"{{white}}"},"black":{"kind":"llm","modelId":"{{black}}"},"plannedStart":"{{start}}","plannedEnd":"2026-10-24T18:30:00Z","state":"{{state}}","gameId":{{(gameId is null ? "null" : $"\"{gameId}\"")}}}""";

    public static string Schedules(string status, int total, int finished) =>
        $$"""[{"id":"66666666-6666-6666-6666-666666666666","name":"Day one","status":"{{status}}","slotsTotal":{{total}},"slotsFinished":{{finished}}}]""";

    /// <summary>A schedule with its pacing, so a replay test runs in milliseconds rather than at the broadcast pace.</summary>
    public static string PacedSchedules(string status, int total, int finished, int moveDelayMs, int gameGapSeconds)
    {
        var pacing = $"{{\"moveDelayMs\":{moveDelayMs},\"gameGapSeconds\":{gameGapSeconds}}}";
        return $"[{{\"id\":\"66666666-6666-6666-6666-666666666666\",\"name\":\"Day one\",\"status\":\"{status}\",\"slotsTotal\":{total},\"slotsFinished\":{finished},\"pacing\":{pacing}}}]";
    }

    public static string Menu(params (string Code, string Label, double PerSide)[] items) =>
        $$"""{"items":[{{string.Join(',', items.Select(i => $$"""{"code":"{{i.Code}}","playerId":"{{i.Label}}","label":"{{i.Label}}","estimatedUsdPerSide":{{i.PerSide.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}"""))}}],"styles":["classic","board","think"],"tiersUsd":[1,2,5,10],"feeShare":0.3,"commandHelp":"Super Chat: !g <white> vs <black> [style]"}""";

    /// <summary>One request as GET /api/requests/queue reports it. The text is the parsed configuration only, never free text.</summary>
    public static string Request(string payer, string white, string black, string status, int? position = null,
        Guid? gameId = null, string? reason = null) =>
        $$"""{"id":"{{Guid.NewGuid()}}","source":"youtube","payerDisplay":"{{payer}}","white":"{{white}}","black":"{{black}}","priceUsd":1,"status":"{{status}}","reason":{{(reason is null ? "null" : $"\"{reason}\"")}},"gameId":{{(gameId is null ? "null" : $"\"{gameId}\"")}},"receivedAt":"2026-10-24T18:00:00Z","queuePosition":{{(position is null ? "null" : position)}} }""";

    public static string Queue(string? playing = null, string queued = "", string recent = "", int? wait = null) =>
        $$"""{"playing":{{playing ?? "null"}},"queued":[{{queued}}],"recent":[{{recent}}],"estimatedWaitMinutes":{{(wait is null ? "null" : wait)}} }""";

    public static string Page(params string[] games) =>
        $$"""{"items":[{{string.Join(',', games)}}],"page":1,"pageSize":50,"total":{{games.Length}}}""";

    public static string Summary(string state, string result, string? termination, int plies) =>
        $$"""{"id":"{{LiveBroadcastServer.GameId}}","tournamentId":null,"white":{"kind":"llm","modelId":"gpt"},"black":{"kind":"llm","modelId":"claude"},"rated":true,"state":"{{state}}","result":"{{result}}","termination":{{(termination is null ? "null" : $"\"{termination}\"")}},"plyCount":{{plies}},"createdAt":"2026-10-24T18:00:00Z","finishedAt":null}""";

    /// <summary>A game as GET /api/games/{id} reports it. Each move is (colour, SAN, FEN after it); ply N is move N.</summary>
    public static string Detail(string state, string result, string? termination,
        params (string Color, string San, string Fen)[] moves)
    {
        var plies = string.Join(',', moves.Select((move, index) => PlyJson(index + 1, move)));
        var current = moves.Length == 0 ? LiveGame.StartFen : moves[^1].Fen;
        return $$"""{"summary":{{Summary(state, result, termination, moves.Length)}},"startFen":"{{LiveGame.StartFen}}","currentFen":"{{current}}","moves":[{{plies}}]}""";
    }

    public static string MoveStarted(int ply, string color, string modelId) =>
        $$"""{"gameId":"{{LiveBroadcastServer.GameId}}","at":"2026-10-24T18:00:00Z","ply":{{ply}},"color":"{{color}}","modelId":"{{modelId}}"}""";

    public static string MovePlayed(int ply, string color, string san, string fen) =>
        $$"""{"gameId":"{{LiveBroadcastServer.GameId}}","at":"2026-10-24T18:00:02Z","ply":{{ply}},"color":"{{color}}","san":"{{san}}","fen":"{{fen}}","attempts":1,"latencyMs":1200}""";

    public static string GameFinished(string result, string termination) =>
        $$"""{"gameId":"{{LiveBroadcastServer.GameId}}","at":"2026-10-24T18:05:00Z","result":"{{result}}","termination":"{{termination}}"}""";

    private static string PlyJson(int ply, (string Color, string San, string Fen) move) =>
        $$"""{"ply":{{ply}},"color":"{{move.Color}}","san":"{{move.San}}","fenAfter":"{{move.Fen}}","attempts":1,"latencyMs":1200,"inputTokens":null,"outputTokens":null,"centipawnLoss":null,"bestSan":null}""";
}
