using Chess.Api.Contracts;
using Chess.Api.Tournaments;

namespace Chess.Api.Broadcast;

/// <summary>One planned game: the output of <see cref="BroadcastPlanner"/>, before it is stored as a slot.</summary>
public sealed record PlannedSlot(int Index, PlayerSpec White, PlayerSpec Black, DateTimeOffset PlannedStart,
    DateTimeOffset PlannedEnd);

/// <summary>The planner's result: the slots in play order and the estimate per model (premium requests, AI credits, USD).</summary>
public sealed record BroadcastPlan(IReadOnlyList<PlannedSlot> Slots,
    IReadOnlyList<ModelPremiumEstimateDto> EstimatedPremiumRequests);

/// <summary>A plan request the planner cannot fill. The API maps it to a validation problem.</summary>
public sealed class InvalidPlanException(string message) : Exception(message);

/// <summary>
/// Pure planner (03 §9.1, ADR-011): builds the slots of a broadcast window. No I/O, no clock, no EF.
/// Deterministic for a given <see cref="PlanRequest"/>, model inputs and cost options.
/// </summary>
public static class BroadcastPlanner
{
    /// <summary>
    /// Fills the window round-robin. Every unordered pair of models meets once per round, in a seeded order that
    /// never repeats the previous pairing. Colours are then assigned over the whole day so that each model's
    /// White and Black counts differ by at most one. A game only starts when it can finish by <c>EndsAt</c>; the
    /// final gap is left empty rather than starting a game that cannot end.
    /// </summary>
    /// <param name="inputs">Per model id: its premium multiplier, prices and stored moves. A missing entry gives null estimates.</param>
    /// <param name="usdPerAiCredit">The USD value of one AI credit (<c>Arena:Cost:UsdPerAiCredit</c>, ADR-012).</param>
    public static BroadcastPlan Plan(PlanRequest request, IReadOnlyDictionary<string, ModelCostInputs> inputs, double usdPerAiCredit)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(inputs);

        var models = Validate(request);
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

        if (pairings.Count == 0)
        {
            throw new InvalidPlanException("The window is too short for one game at the assumed pace.");
        }

        var colours = Colour(pairings, models.Count);
        var slots = windows.Select((window, index) => new PlannedSlot(index,
            SpecFor(models[colours[index].White]), SpecFor(models[colours[index].Black]), window.Start, window.End)).ToList();

        return new BroadcastPlan(slots, models.Select(model => EstimateFor(model,
            slots.Count(slot => slot.White.ModelId == model || slot.Black.ModelId == model),
            request.AssumedPlies, inputs, usdPerAiCredit)).ToList());
    }

    private static IReadOnlyList<string> Validate(PlanRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new InvalidPlanException("Name is required.");
        }

        if (request.Models.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidPlanException("Model ids must not be blank.");
        }

        var models = request.Models.Distinct(StringComparer.Ordinal).ToList();
        if (models.Count < 2)
        {
            throw new InvalidPlanException("At least two distinct models are required.");
        }

        if (request.DurationHours < 1)
        {
            throw new InvalidPlanException("DurationHours must be at least 1.");
        }

        if (request.Pacing.MoveDelayMs < 0 || request.Pacing.GameGapSeconds < 0)
        {
            throw new InvalidPlanException("Pacing values must not be negative.");
        }

        if (!(request.AssumedPlies > 0))
        {
            throw new InvalidPlanException("AssumedPlies must be positive.");
        }

        if (request.AssumedMoveLatencyMs < 0)
        {
            throw new InvalidPlanException("AssumedMoveLatencyMs must not be negative.");
        }

        if (request.AssumedMoveLatencyMs + request.Pacing.MoveDelayMs <= 0)
        {
            throw new InvalidPlanException("A game must take a positive amount of time.");
        }

        return models;
    }

    // Pairs are (i, j) with i < j, so a pairing compares equal regardless of colour.
    private static List<(int A, int B)> AllPairs(int count) =>
        [.. from i in Enumerable.Range(0, count)
            from j in Enumerable.Range(i + 1, count - i - 1)
            select (i, j)];

    // Prefers a pair that shares no model with the previous game, then one that is not the same pairing.
    // The fallbacks only apply when nothing better is left (with three models every two pairs share one model).
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
        new(model == ModelCatalog.RandomModelId ? "random" : "llm", model);

    /// <summary>
    /// One model's estimate over a day of <paramref name="slotsForModel"/> games: the premium requests are slots × (plies / 2)
    /// moves a side × its multiplier, and the AI credits and USD come from <see cref="CostEstimator"/> (03 §9.1, §10.2).
    /// The random player costs nothing. Also used by the API for a stored schedule, so a plan and its saved copy agree.
    /// </summary>
    public static ModelPremiumEstimateDto EstimateFor(string model, int slotsForModel, double assumedPlies,
        IReadOnlyDictionary<string, ModelCostInputs> inputs, double usdPerAiCredit)
    {
        // A variant (model@effort) is priced and rated with its base model (03 §10.2, ADR-012).
        var baseModel = ModelVariant.BaseOf(model);
        var entry = inputs.GetValueOrDefault(baseModel);
        var moves = slotsForModel * (assumedPlies / 2);
        var cost = CostEstimator.Estimate(baseModel, moves, entry, usdPerAiCredit);

        double? premium = baseModel == ModelCatalog.RandomModelId ? 0
            : entry?.PremiumMultiplier is { } multiplier ? moves * multiplier
            : null;
        return new ModelPremiumEstimateDto(model, premium, cost.AiCredits, cost.Usd, cost.Basis, cost.SampleMoves);
    }
}
