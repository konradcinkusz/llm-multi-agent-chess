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
/// The planner of 03 §9.1 as the planner page runs it before anything is saved. It produces the slots and the
/// premium estimate that <c>POST /api/broadcast/plan</c> would store for the same request, so the day can be
/// previewed before a draft exists. It is a port of <c>Chess.Api.Broadcast.BroadcastPlanner</c> (the Web project may
/// not reference Chess.Api); <c>PlanPlannerTests</c> holds the two equal across seeds and model counts.
/// </summary>
public static class PlanPlanner
{
    /// <summary>The API's longest window, in hours (<c>BroadcastEndpoints.MaxDurationHours</c>).</summary>
    public const int MaxDurationHours = 168;

    public const string RandomModelId = "random";

    /// <summary>Why the request cannot be planned, empty when it can. The API checks the same rules and answers with a problem.</summary>
    public static IReadOnlyList<string> Problems(PlanRequest request)
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            problems.Add("Give the schedule a name.");
        }

        if (request.Models.Distinct(StringComparer.Ordinal).Count() < 2)
        {
            problems.Add("Choose at least two models.");
        }

        if (request.DurationHours is < 1 or > MaxDurationHours)
        {
            problems.Add($"Hours must be between 1 and {MaxDurationHours}.");
        }

        if (request.Pacing.MoveDelayMs < 0 || request.Pacing.GameGapSeconds < 0)
        {
            problems.Add("Pacing values must not be negative.");
        }

        if (request.AssumedMoveLatencyMs + request.Pacing.MoveDelayMs <= 0)
        {
            problems.Add("A game must take a positive amount of time.");
        }

        // Only a request that is otherwise valid can be asked for its games.
        if (problems.Count == 0 && Plan(request).Count == 0)
        {
            problems.Add("The window is too short for one game at the assumed pace.");
        }

        return problems;
    }

    /// <summary>
    /// The slots of the window in play order, each with its own window. Empty when no game fits. Call only when
    /// <see cref="Problems"/> is empty.
    /// </summary>
    public static IReadOnlyList<SlotEdit> Plan(PlanRequest request)
    {
        var models = request.Models.Distinct(StringComparer.Ordinal).ToList();
        var gameLength = TimeSpan.FromMilliseconds(request.AssumedPlies * (request.AssumedMoveLatencyMs + request.Pacing.MoveDelayMs));
        var gap = TimeSpan.FromSeconds(request.Pacing.GameGapSeconds);
        var endsAt = request.StartsAt.AddHours(request.DurationHours);

        var rng = new Random(request.Seed);
        var pool = new List<(int A, int B)>();
        (int A, int B)? last = null;
        var pairings = new List<(int A, int B)>();
        var windows = new List<(DateTimeOffset Start, DateTimeOffset End)>();
        var start = request.StartsAt;

        while (true)
        {
            if (pool.Count == 0)
            {
                pool = AllPairs(models.Count);
            }

            var index = ChoosePair(pool, last, rng);
            var pairing = pool[index];
            pool.RemoveAt(index);

            var end = start + gameLength;
            if (end > endsAt)
            {
                break;
            }

            pairings.Add(pairing);
            windows.Add((start, end));
            start = end + gap;
            last = pairing;
        }

        var colours = Colour(pairings, models.Count);
        return windows.Select((window, index) => new SlotEdit(SpecFor(models[colours[index].White]),
            SpecFor(models[colours[index].Black]), window.Start, window.End)).ToList();
    }

    /// <summary>
    /// Premium requests estimated for <paramref name="model"/>: slots it plays × (plies / 2) moves a side × its
    /// multiplier (03 §9.1). Null when the multiplier is unknown; the random player costs nothing.
    /// </summary>
    public static double? Requests(string model, IReadOnlyList<SlotEdit> slots, double assumedPlies,
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

        var slotsForModel = slots.Count(slot => slot.White.ModelId == model || slot.Black.ModelId == model);
        return slotsForModel * (assumedPlies / 2) * value;
    }

    // Pairs are (i, j) with i < j, so a pairing compares equal regardless of colour.
    private static List<(int A, int B)> AllPairs(int count) =>
        [.. from i in Enumerable.Range(0, count)
            from j in Enumerable.Range(i + 1, count - i - 1)
            select (i, j)];

    // Prefers a pair that shares no model with the previous game, then one that is not the same pairing.
    private static int ChoosePair(List<(int A, int B)> pool, (int A, int B)? last, Random rng)
    {
        bool NotRepeated((int A, int B) p) => last is not { } l || p != l;
        bool Disjoint((int A, int B) p) => last is not { } l || !Shares(p, l);

        Func<(int A, int B), bool>[] preferences = [p => NotRepeated(p) && Disjoint(p), NotRepeated, _ => true];

        foreach (var prefer in preferences)
        {
            var candidates = Enumerable.Range(0, pool.Count).Where(i => prefer(pool[i])).ToList();
            if (candidates.Count > 0)
            {
                return candidates[rng.Next(candidates.Count)];
            }
        }

        throw new InvalidOperationException("The pool of pairings is empty.");
    }

    private static bool Shares((int A, int B) pair, (int A, int B) other) =>
        pair.A == other.A || pair.A == other.B || pair.B == other.A || pair.B == other.B;

    // Gives each game a colour so that every model's White and Black counts differ by at most one over the day.
    // Models are vertices and games are edges. A dummy vertex is joined to each odd-degree model, which makes every
    // degree even. Walking closed trails and orienting each edge along its walk gives in = out at every vertex, and
    // deleting the dummy edges leaves at most one unmatched edge per model.
    private static IReadOnlyList<(int White, int Black)> Colour(IReadOnlyList<(int A, int B)> games, int modelCount)
    {
        var dummy = modelCount;
        var edges = new List<(int U, int V)>(games);
        var degree = new int[modelCount];
        foreach (var (a, b) in games)
        {
            degree[a]++;
            degree[b]++;
        }

        for (var model = 0; model < modelCount; model++)
        {
            if (degree[model] % 2 == 1)
            {
                edges.Add((model, dummy));
            }
        }

        var incident = Enumerable.Range(0, modelCount + 1).Select(_ => new List<int>()).ToArray();
        for (var e = 0; e < edges.Count; e++)
        {
            incident[edges[e].U].Add(e);
            incident[edges[e].V].Add(e);
        }

        var used = new bool[edges.Count];
        var scan = new int[modelCount + 1];
        var from = new int[edges.Count];

        int? NextUnused(int vertex)
        {
            while (scan[vertex] < incident[vertex].Count)
            {
                var e = incident[vertex][scan[vertex]];
                if (!used[e])
                {
                    return e;
                }

                scan[vertex]++;
            }

            return null;
        }

        for (var start = 0; start <= dummy; start++)
        {
            var current = start;
            while (NextUnused(current) is { } e)
            {
                used[e] = true;
                from[e] = current;
                current = edges[e].U == current ? edges[e].V : edges[e].U;
            }
        }

        return Enumerable.Range(0, games.Count)
            .Select(i => (White: from[i], Black: edges[i].U == from[i] ? edges[i].V : edges[i].U))
            .ToList();
    }

    private static PlayerSpec SpecFor(string model) =>
        new(model == RandomModelId ? "random" : "llm", model);
}
