using System.Net;
using System.Text.Json;
using System.Web;
using Chess.Web.Api;
using Chess.Web.Tests.Site.Game;
using Chess.Web.Tests.Support;

namespace Chess.Web.Tests.Site;

/// <summary>
/// A fixed snapshot of the arena for the export tests: three models, one tournament, 24 finished games (one aborted), and
/// two games still open that the games list must leave out. Every number is canned; nothing is computed by the API.
/// </summary>
internal static class SiteDataSet
{
    public static readonly DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    public static readonly Guid Tournament = Guid.Parse("7d3a5c1e-0000-4000-8000-000000000001");

    public const int FinishedGames = 24;

    public static readonly string[] Models = ["alpha", "beta", "gamma"];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>A game that was still in play when the export ran. Not listed, not fetched for usage.</summary>
    public static readonly Guid OpenGame = Guid.Parse("00000000-0000-4000-8000-0000000000aa");

    public static readonly Guid QueuedGame = Guid.Parse("00000000-0000-4000-8000-0000000000bb");

    public static Guid GameId(int index) => Guid.Parse($"00000000-0000-4000-8000-{index:000000000000}");

    /// <summary>Answers the API calls the export makes. An unknown call is a 404, so a missed or new call fails the test.</summary>
    public static HttpResponseMessage Respond(RecordedRequest request, bool empty)
    {
        var uri = new Uri(new Uri("http://api"), request.Target);
        var query = HttpUtility.ParseQueryString(uri.Query);
        var path = uri.AbsolutePath;

        return path switch
        {
            "/api/ratings" => Body(empty ? Leaderboard([], 0) : query["tournamentId"] is { } t
                ? TournamentLeaderboard(Guid.Parse(t))
                : Overall()),
            "/api/ratings/matrix" => Body(empty ? new MatrixDto([], []) : Matrix()),
            "/api/ratings/history" => Body(empty ? new List<RatingHistoryPointDto>() : History(query["model"]!)),
            "/api/results/breakdown" => Body(empty ? new List<ModelResultsDto>() : Breakdown()),
            "/api/tournaments" => Body(empty ? new List<TournamentDto>() : [Spring()]),
            "/api/games" => Body(GamesPage(empty, int.Parse(query["page"]!, System.Globalization.CultureInfo.InvariantCulture))),
            _ when path.StartsWith("/api/games/", StringComparison.Ordinal) && path.EndsWith("/usage", StringComparison.Ordinal) =>
                Body(Usage(Guid.Parse(path.Split('/')[3]))),
            _ => SiteGameDataSet.Respond(path, query, empty ? [] : Games()) ?? new HttpResponseMessage(HttpStatusCode.NotFound),
        };
    }

    private static HttpResponseMessage Body<T>(T value) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(value, Json), System.Text.Encoding.UTF8, "application/json"),
        };

    private static LeaderboardDto Overall() => Leaderboard(
    [
        new ModelRating("alpha", 1600, 1560, 1640, 16, 0.70, 0.98, null, 0.60, 70, false),
        new ModelRating("beta", 1520, 1480, 1560, 16, 0.50, 0.95, null, 0.50, 50, false),
        new ModelRating("gamma", 1450, 1380, 1520, 8, 0.30, 0.90, null, 0.40, 30, true),
    ], FinishedGames);

    private static LeaderboardDto TournamentLeaderboard(Guid id) =>
        id == Tournament
            ? Leaderboard([new ModelRating("alpha", 1580, 1540, 1620, 12, 0.65, 0.97, null, 0.55, 65, false),
                new ModelRating("beta", 1500, 1460, 1540, 12, 0.35, 0.94, null, 0.45, 35, false)], 12)
            : Leaderboard([], 0);

    private static LeaderboardDto Leaderboard(IReadOnlyList<ModelRating> models, int games) =>
        new(models, 12, games, false);

    private static MatrixDto Matrix()
    {
        var cells = new List<PairwiseCoefficient>();
        void Pair(string a, string b, double rqc, double low, double high, int games, double score)
        {
            cells.Add(Cell(a, b, rqc, low, high, games, score));
            cells.Add(Cell(b, a, 1 - rqc, 1 - high, 1 - low, games, 1 - score));
        }

        Pair("alpha", "beta", 0.62, 0.55, 0.70, 8, 5);
        Pair("alpha", "gamma", 0.71, 0.60, 0.80, 4, 3);
        Pair("beta", "gamma", 0.50, 0.40, 0.60, 4, 2);
        return new MatrixDto(Models, cells);
    }

    private static PairwiseCoefficient Cell(string a, string b, double rqc, double low, double high, int games, double score) =>
        new(a, b, rqc, rqc - 0.1, rqc, low, high, 100 * (rqc - 0.5), games, score);

    private static IReadOnlyList<RatingHistoryPointDto> History(string model) =>
    [
        new RatingHistoryPointDto(Now.AddDays(-2), 4, model, 1500, 1400, 1600, 50, true),
        new RatingHistoryPointDto(Now.AddDays(-1), 12, model, 1560, 1500, 1620, 60, false),
        new RatingHistoryPointDto(Now, 24, model, 1600, 1560, 1640, 70, false),
    ];

    private static IReadOnlyList<ModelResultsDto> Breakdown() =>
    [
        new("alpha", 16, 10, 2, 4, 1, 300, 0.98, 1200, 900, 300, 42),
        new("beta", 16, 6, 4, 6, 0, 290, 0.95, 2100, 950, 320, 55),
        new("gamma", 8, 2, 2, 4, 2, 140, 0.90, 800, 700, 200, null),
    ];

    private static TournamentDto Spring() =>
        new(Tournament, "Spring", ["alpha", "beta"], 3, "running", 12, 12, Now.AddDays(-1), null);

    /// <summary>Finished games newest first, then the two open ones. Game <c>i</c> is older than game <c>i - 1</c>.</summary>
    private static PageDto<GameSummaryDto> GamesPage(bool empty, int page)
    {
        var all = empty ? new List<GameSummaryDto>() : Games();
        const int pageSize = 200;
        var items = all.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return new PageDto<GameSummaryDto>(items, page, pageSize, all.Count);
    }

    private static List<GameSummaryDto> Games()
    {
        var games = new List<GameSummaryDto>();
        for (var i = 0; i < FinishedGames; i++)
        {
            var white = Models[i % 3];
            var black = Models[(i + 1) % 3];
            var aborted = i == 5;
            var result = aborted ? "*" : (i % 3) switch { 0 => "1-0", 1 => "0-1", _ => "1/2-1/2" };
            games.Add(new GameSummaryDto(GameId(i), i < 12 ? Tournament : null,
                new PlayerSpec("llm", white), new PlayerSpec("llm", black), true,
                "finished", result, aborted ? "aborted" : "checkmate", 40 + i,
                Now.AddMinutes(-i), Now.AddMinutes(-i).AddSeconds(30)));
        }

        games.Add(new GameSummaryDto(OpenGame, null, new PlayerSpec("llm", "alpha"), new PlayerSpec("llm", "beta"), true,
            "running", "*", null, 7, Now, null));
        games.Add(new GameSummaryDto(QueuedGame, null, new PlayerSpec("llm", "beta"), new PlayerSpec("llm", "gamma"), true,
            "queued", "*", null, 0, Now, null));
        return games.OrderByDescending(g => g.CreatedAt).ToList();
    }

    private static GameUsageDto Usage(Guid id) =>
        new(id,
            new SideUsageDto("white", "alpha", 40, 52_000, 9_000, 1_200, 0, 1.25, 0.0125, "estimated"),
            new SideUsageDto("black", "beta", 41, 53_000, 9_400, 0, 0, 1.5, 0.015, "estimated"),
            2.75, 0.0275, 0.01);
}
