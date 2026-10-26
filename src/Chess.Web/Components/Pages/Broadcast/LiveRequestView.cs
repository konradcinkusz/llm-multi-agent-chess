using System.Globalization;
using Chess.Web.Api;

namespace Chess.Web.Components.Pages.Broadcast;

/// <summary>
/// What the OBS page shows of the viewer requests (03 §15.6), as plain functions of the queue and the menu. A request
/// carries only its parsed configuration and the payer's display name, so nothing here can show the viewer's free text.
/// </summary>
public static class LiveRequestView
{
    /// <summary>How many queued requests the page lists.</summary>
    public const int MaxQueued = 3;

    /// <summary>How many recent rejections the page lists.</summary>
    public const int MaxRejections = 2;

    /// <summary>The rejection line of 03 §15.6. It names the payer and the menu, and never says why or what was typed.</summary>
    public const string RejectionText = "credit kept, see the menu";

    /// <summary>True while a request is running or waiting, so the stream does not start a replay.</summary>
    public static bool HasWaiting(RequestQueueDto? queue) =>
        queue is not null && (queue.Playing is not null || queue.Queued.Count > 0);

    /// <summary>"@name" when the game on air was requested, else null.</summary>
    public static string? RequestedBy(RequestQueueDto? queue, Guid? gameId) =>
        queue?.Playing is { GameId: { } playingId } playing && playingId == gameId ? playing.PayerDisplay : null;

    /// <summary>The estimated wait as the stream says it: "starts next", "about 12 min", or nothing when the API has no estimate.</summary>
    public static string WaitLabel(int? minutes) => minutes switch
    {
        null => string.Empty,
        <= 0 => "starts next",
        1 => "about 1 min",
        _ => $"about {minutes} min",
    };

    public static IReadOnlyList<string> RejectionLines(RequestQueueDto? queue) =>
        queue is null
            ? []
            : queue.Recent
                .Where(request => request.Status == "rejected")
                .Take(MaxRejections)
                .Select(request => $"@{request.PayerDisplay}: {RejectionText}")
                .ToList();

    /// <summary>A price in dollars, as "$1.50".</summary>
    public static string Price(double usd) => "$" + usd.ToString("0.00", CultureInfo.InvariantCulture);
}

/// <summary>
/// Which stored game the stream replays while the request queue is empty (03 §15.6): the best-rated recent finished game,
/// with the game just shown left out. Pure: the callers do the reads and set the pace.
/// </summary>
public static class ReplayPicker
{
    /// <summary>How many recent finished games are candidates. The games list is newest first.</summary>
    public const int Pool = 50;

    /// <summary>
    /// The finished, rated game whose two models have the highest total rating, ties going to the newer game. A model
    /// without a rating counts as zero. When the only candidate is <paramref name="avoid"/>, that game is returned again.
    /// </summary>
    public static GameSummaryDto? Pick(IReadOnlyList<GameSummaryDto> newestFirst, LeaderboardDto? board, Guid? avoid)
    {
        var ratings = board?.Models.ToDictionary(model => model.Model, model => model.Rating, StringComparer.Ordinal)
            ?? new Dictionary<string, double>(StringComparer.Ordinal);
        var pool = newestFirst
            .Where(game => game.State == "finished" && game.Rated && game.PlyCount > 0)
            .Take(Pool)
            .ToList();

        var choice = pool
            .Where(game => game.Id != avoid)
            .Select((game, index) => (Game: game, Index: index))
            .OrderByDescending(entry => Total(entry.Game, ratings))
            .ThenBy(entry => entry.Index)
            .Select(entry => entry.Game)
            .FirstOrDefault();
        return choice ?? pool.FirstOrDefault();
    }

    /// <summary>The game as it stands after <paramref name="plies"/> plies, so the page shows the same parts a live game does.</summary>
    public static GameDetailDto UpTo(GameDetailDto game, int plies)
    {
        var moves = game.Moves.Take(plies).ToList();
        return game with { CurrentFen = moves.Count == 0 ? game.StartFen : moves[^1].FenAfter, Moves = moves };
    }

    private static double Total(GameSummaryDto game, IReadOnlyDictionary<string, double> ratings) =>
        RatingOf(game.White.ModelId, ratings) + RatingOf(game.Black.ModelId, ratings);

    private static double RatingOf(string model, IReadOnlyDictionary<string, double> ratings) =>
        ratings.TryGetValue(model, out var rating) ? rating : 0;
}
