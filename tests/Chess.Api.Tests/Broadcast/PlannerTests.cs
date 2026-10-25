using Chess.Api.Broadcast;
using Chess.Api.Contracts;
using Chess.Api.Tournaments;
using Xunit;

namespace Chess.Api.Tests.Broadcast;

public class PlannerTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 25, 0, 0, 0, TimeSpan.Zero);
    private static readonly PacingProfile DefaultPacing = new(MoveDelayMs: 4000, GameGapSeconds: 20);

    // Defaults of 03 §9.1 at an assumed 3 s per move: 80 plies × 7 s = 560 s per game, plus a 20 s gap.
    private static readonly TimeSpan GameLength = TimeSpan.FromSeconds(560);
    private static readonly TimeSpan Gap = TimeSpan.FromSeconds(20);

    private static readonly IReadOnlyDictionary<string, ModelCostInputs> NoInputs = new Dictionary<string, ModelCostInputs>();
    private const double Costs = 0.01;

    private static IReadOnlyDictionary<string, ModelCostInputs> Multipliers(params (string Id, double? Multiplier)[] models) =>
        models.ToDictionary(m => m.Id, m => new ModelCostInputs(m.Multiplier, null, null), StringComparer.Ordinal);

    private static PlanRequest Request(IEnumerable<string> models, int seed = 20261024, int durationHours = 24,
        PacingProfile? pacing = null, double assumedMoveLatencyMs = 3000) =>
        new("Test day", models.ToList(), Start, durationHours, pacing ?? DefaultPacing, seed,
            AssumedPlies: 80, AssumedMoveLatencyMs: assumedMoveLatencyMs);

    private static BroadcastPlan PlanFor(params string[] models) =>
        BroadcastPlanner.Plan(Request(models), NoInputs, Costs);

    private static IEnumerable<(string Whi, string Blk)> Pairings(BroadcastPlan plan) =>
        plan.Slots.Select(s => (s.White.ModelId, s.Black.ModelId));

    [Fact]
    public void Default_24h_window_with_three_models_fills_the_window_within_one_game()
    {
        var plan = PlanFor("fake-a", "fake-b", "fake-c");

        var last = plan.Slots[^1];
        var end = Start.AddHours(24);
        Assert.True(last.PlannedEnd <= end, "the last slot must end inside the window");
        Assert.True(end - last.PlannedEnd < GameLength + Gap,
            "no further game could start and finish inside the window, so the leftover is under one slot");
    }

    [Fact]
    public void Slots_are_back_to_back_with_the_game_gap_between_them()
    {
        var plan = PlanFor("fake-a", "fake-b", "fake-c");

        Assert.Equal(Start, plan.Slots[0].PlannedStart);
        foreach (var slot in plan.Slots)
        {
            Assert.Equal(GameLength, slot.PlannedEnd - slot.PlannedStart);
        }
        foreach (var (previous, next) in plan.Slots.Zip(plan.Slots.Skip(1)))
        {
            Assert.Equal(Gap, next.PlannedStart - previous.PlannedEnd);
        }
        Assert.Equal(Enumerable.Range(0, plan.Slots.Count), plan.Slots.Select(s => s.Index));
    }

    [Fact]
    public void Same_request_gives_the_same_plan()
    {
        var first = BroadcastPlanner.Plan(Request(["fake-a", "fake-b", "fake-c"]), NoInputs, Costs);
        var second = BroadcastPlanner.Plan(Request(["fake-a", "fake-b", "fake-c"]), NoInputs, Costs);

        Assert.Equal(first.Slots, second.Slots);
    }

    [Fact]
    public void Different_seed_gives_a_different_order()
    {
        var one = BroadcastPlanner.Plan(Request(["fake-a", "fake-b", "fake-c", "fake-d"], seed: 1), NoInputs, Costs);
        var two = BroadcastPlanner.Plan(Request(["fake-a", "fake-b", "fake-c", "fake-d"], seed: 2), NoInputs, Costs);

        Assert.NotEqual(Pairings(one), Pairings(two));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void Every_unordered_pair_meets_and_no_pairing_repeats_back_to_back(int modelCount)
    {
        var models = Enumerable.Range(0, modelCount).Select(i => $"fake-{i}").ToList();
        var plan = BroadcastPlanner.Plan(Request(models), NoInputs, Costs);

        var unordered = plan.Slots.Select(s => string.CompareOrdinal(s.White.ModelId, s.Black.ModelId) < 0
            ? (s.White.ModelId, s.Black.ModelId)
            : (s.Black.ModelId, s.White.ModelId)).ToList();

        Assert.Equal(modelCount * (modelCount - 1) / 2, unordered.Distinct().Count());
        Assert.All(unordered.Zip(unordered.Skip(1)), consecutive => Assert.NotEqual(consecutive.First, consecutive.Second));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void Colours_stay_within_one_game_of_balance_for_every_model(int modelCount)
    {
        var models = Enumerable.Range(0, modelCount).Select(i => $"fake-{i}").ToList();
        var plan = BroadcastPlanner.Plan(Request(models), NoInputs, Costs);

        foreach (var model in models)
        {
            var whites = plan.Slots.Count(s => s.White.ModelId == model);
            var blacks = plan.Slots.Count(s => s.Black.ModelId == model);
            Assert.True(Math.Abs(whites - blacks) <= 1, $"{model}: {whites} white vs {blacks} black");
        }
    }

    [Fact]
    public void Colour_balance_holds_for_every_seed_not_just_the_default()
    {
        for (var seed = 1; seed <= 20; seed++)
        {
            var models = Enumerable.Range(0, 3 + seed % 4).Select(i => $"fake-{i}").ToList();
            var plan = BroadcastPlanner.Plan(Request(models, seed: seed), NoInputs, Costs);

            foreach (var model in models)
            {
                var whites = plan.Slots.Count(s => s.White.ModelId == model);
                var blacks = plan.Slots.Count(s => s.Black.ModelId == model);
                Assert.True(Math.Abs(whites - blacks) <= 1, $"seed {seed}, {model}: {whites} white vs {blacks} black");
            }
        }
    }

    [Fact]
    public void Random_is_allowed_as_a_model_and_costs_no_premium_requests()
    {
        var plan = BroadcastPlanner.Plan(Request(["random", "fake-a"]),
            Multipliers(("fake-a", 1)), Costs);

        var randomSide = plan.Slots.SelectMany(s => new[] { s.White, s.Black }).Where(p => p.ModelId == "random").ToList();
        Assert.NotEmpty(randomSide);
        Assert.All(randomSide, p => Assert.Equal("random", p.Kind));
        Assert.All(plan.Slots.SelectMany(s => new[] { s.White, s.Black }).Where(p => p.ModelId == "fake-a"),
            p => Assert.Equal("llm", p.Kind));
        Assert.Equal(0, plan.EstimatedPremiumRequests.Single(e => e.ModelId == "random").EstimatedPremiumRequests);
    }

    [Fact]
    public void Premium_estimate_is_slots_per_model_times_half_the_plies_times_multiplier()
    {
        var plan = BroadcastPlanner.Plan(Request(["fake-a", "fake-b", "fake-c"]),
            Multipliers(("fake-a", 3), ("fake-b", null)), Costs);

        var slotsForA = plan.Slots.Count(s => s.White.ModelId == "fake-a" || s.Black.ModelId == "fake-a");
        Assert.Equal(slotsForA * 40 * 3.0, plan.EstimatedPremiumRequests.Single(e => e.ModelId == "fake-a").EstimatedPremiumRequests);
        Assert.Null(plan.EstimatedPremiumRequests.Single(e => e.ModelId == "fake-b").EstimatedPremiumRequests);
        Assert.Null(plan.EstimatedPremiumRequests.Single(e => e.ModelId == "fake-c").EstimatedPremiumRequests);
    }

    [Fact]
    public void Cost_estimate_is_expected_moves_times_tokens_times_prices_in_credits_and_usd()
    {
        // $3 in and $15 out per 1M tokens. fake-a has 5 stored moves averaging 1 000 in and 200 out; fake-b has none.
        var inputs = new Dictionary<string, ModelCostInputs>
        {
            ["fake-a"] = new(3, new ModelPricingDto(3.0, 15.0, null), new ModelTokenAverage(5, 1000, 200)),
            ["fake-b"] = new(null, new ModelPricingDto(3.0, 15.0, null), null),
        };

        var plan = BroadcastPlanner.Plan(Request(["fake-a", "fake-b", "fake-c"]), inputs, Costs);

        var slotsForA = plan.Slots.Count(s => s.White.ModelId == "fake-a" || s.Black.ModelId == "fake-a");
        var moves = slotsForA * 40.0;
        var a = plan.EstimatedPremiumRequests.Single(e => e.ModelId == "fake-a");
        Assert.Equal(moves * 3.0, a.EstimatedPremiumRequests);
        Assert.Equal(moves * (1000 * 3.0 + 200 * 15.0) / 1_000_000, a.EstimatedUsd!.Value, precision: 9);
        Assert.Equal(a.EstimatedUsd!.Value / Costs, a.EstimatedAiCredits!.Value, precision: 9);
        Assert.Equal(CostEstimator.HistoryBasis, a.TokenBasis);
        Assert.Equal(5, a.SampleMoves);

        var slotsForB = plan.Slots.Count(s => s.White.ModelId == "fake-b" || s.Black.ModelId == "fake-b");
        var b = plan.EstimatedPremiumRequests.Single(e => e.ModelId == "fake-b");
        Assert.Null(b.EstimatedPremiumRequests);
        Assert.Equal(slotsForB * 40.0 * (700 * 3.0 + 300 * 15.0) / 1_000_000, b.EstimatedUsd!.Value, precision: 9);
        Assert.Equal(CostEstimator.AssumedBasis, b.TokenBasis);
        Assert.Null(b.SampleMoves);

        var c = plan.EstimatedPremiumRequests.Single(e => e.ModelId == "fake-c");
        Assert.Null(c.EstimatedAiCredits);
        Assert.Null(c.EstimatedUsd);
        Assert.Equal(CostEstimator.UnknownBasis, c.TokenBasis);
    }

    [Theory]
    [InlineData("fake-a")]
    [InlineData("fake-a", "fake-a")]
    public void Fewer_than_two_distinct_models_is_a_validation_error(params string[] models)
    {
        Assert.Throws<InvalidPlanException>(() => BroadcastPlanner.Plan(Request(models), NoInputs, Costs));
    }

    [Fact]
    public void Blank_model_id_is_a_validation_error()
    {
        Assert.Throws<InvalidPlanException>(() => BroadcastPlanner.Plan(Request(["fake-a", " "]), NoInputs, Costs));
    }

    [Fact]
    public void Negative_pacing_is_a_validation_error()
    {
        Assert.Throws<InvalidPlanException>(() => BroadcastPlanner.Plan(
            Request(["fake-a", "fake-b"], pacing: new PacingProfile(-1, 20)), NoInputs, Costs));
    }

    [Fact]
    public void Window_too_short_for_one_game_is_a_validation_error()
    {
        // One game takes 80 plies × 50 min = 4 000 min, far longer than the one-hour window.
        Assert.Throws<InvalidPlanException>(() => BroadcastPlanner.Plan(
            Request(["fake-a", "fake-b"], durationHours: 1, assumedMoveLatencyMs: 3_000_000), NoInputs, Costs));
    }
}
