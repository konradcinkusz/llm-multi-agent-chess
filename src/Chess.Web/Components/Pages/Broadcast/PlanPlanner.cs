using Chess.Web.Api;

namespace Chess.Web.Components.Pages.Broadcast;

/// <summary>Schedule status values (03 §9.1), as the API sends them.</summary>
public static class ScheduleStatuses
{
    public const string Draft = "draft";
    public const string Scheduled = "scheduled";
    public const string Running = "running";
    public const string Paused = "paused";
    public const string Finished = "finished";
    public const string Cancelled = "cancelled";
}

/// <summary>
/// What the planner page needs of the planner of 03 §9.1: the limits the API enforces (so the page can say so before
/// anything is sent) and the premium-request estimate of the games on screen. The API lays the games out and saves them
/// (<c>POST /api/broadcast/schedules</c>); this project may not reference <c>Chess.Api</c>, so the limits are copied.
/// </summary>
public static class PlanPlanner
{
    /// <summary>The API's longest window, in hours (<c>BroadcastEndpoints.MaxDurationHours</c>).</summary>
    public const int MaxDurationHours = 168;

    /// <summary>The most games a schedule holds (<c>BroadcastPlanner.MaxSlots</c>).</summary>
    public const int MaxGames = 500;

    public const string RandomModelId = "random";

    /// <summary>
    /// Premium requests estimated for <paramref name="model"/>: games it plays × (plies / 2) moves a side × its
    /// multiplier (03 §9.1). Null when the multiplier is unknown; the random player costs nothing.
    /// </summary>
    public static double? Requests(string model, IReadOnlyList<ScheduleGame> games, double assumedPlies,
        IReadOnlyDictionary<string, double?> multipliers)
    {
        if (model == RandomModelId)
        {
            return 0;
        }

        // A styled player is billed as its model (03 §14.4): its multiplier is the model's.
        if (!multipliers.TryGetValue(PlayerId.Parse(model).Model, out var multiplier) || multiplier is not { } value)
        {
            return null;
        }

        var gamesForModel = games.Count(game => game.White.ModelId == model || game.Black.ModelId == model);
        return gamesForModel * (assumedPlies / 2) * value;
    }
}
