using Chess.Api.Broadcast;
using Chess.Api.Contracts;
using Xunit;

namespace Chess.Api.Tests.Broadcast;

public class PlannerTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 25, 0, 0, 0, TimeSpan.Zero);
    private static readonly PacingProfile DefaultPacing = new(MoveDelayMs: 4000, GameGapSeconds: 20);

    // Defaults of 03 §9.1 at an assumed 3 s per move: 80 plies × 7 s = 560 s per game, plus a 20 s gap.
    private static readonly TimeSpan GameLength = TimeSpan.FromSeconds(560);
    private static readonly TimeSpan Gap = TimeSpan.FromSeconds(20);

    private static readonly IReadOnlyDictionary<string, double?> NoMultipliers = new Dictionary<string, double?>();

    private static PlanRequest Request(IEnumerable<string> models, int seed = 20261024, int durationHours = 24,
        PacingProfile? pacing = null, double assumedMoveLatencyMs = 3000) =>
        new("Test day", models.ToList(), Start, durationHours, pacing ?? DefaultPacing, seed,
            AssumedPlies: 80, AssumedMoveLatencyMs: assumedMoveLatencyMs);

    private static BroadcastPlan PlanFor(params string[] models) =>
        BroadcastPlanner.Plan(Request(models), NoMultipliers);

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
        var first = BroadcastPlanner.Plan(Request(["fake-a", "fake-b", "fake-c"]), NoMultipliers);
        var second = BroadcastPlanner.Plan(Request(["fake-a", "fake-b", "fake-c"]), NoMultipliers);

        Assert.Equal(first.Slots, second.Slots);
    }

    [Fact]
    public void Different_seed_gives_a_different_order()
    {
        var one = BroadcastPlanner.Plan(Request(["fake-a", "fake-b", "fake-c", "fake-d"], seed: 1), NoMultipliers);
        var two = BroadcastPlanner.Plan(Request(["fake-a", "fake-b", "fake-c", "fake-d"], seed: 2), NoMultipliers);

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
        var plan = BroadcastPlanner.Plan(Request(models), NoMultipliers);

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
        var plan = BroadcastPlanner.Plan(Request(models), NoMultipliers);

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
            var plan = BroadcastPlanner.Plan(Request(models, seed: seed), NoMultipliers);

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
            new Dictionary<string, double?> { ["fake-a"] = 1 });

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
            new Dictionary<string, double?> { ["fake-a"] = 3, ["fake-b"] = null });

        var slotsForA = plan.Slots.Count(s => s.White.ModelId == "fake-a" || s.Black.ModelId == "fake-a");
        Assert.Equal(slotsForA * 40 * 3.0, plan.EstimatedPremiumRequests.Single(e => e.ModelId == "fake-a").EstimatedPremiumRequests);
        Assert.Null(plan.EstimatedPremiumRequests.Single(e => e.ModelId == "fake-b").EstimatedPremiumRequests);
        Assert.Null(plan.EstimatedPremiumRequests.Single(e => e.ModelId == "fake-c").EstimatedPremiumRequests);
    }

    [Theory]
    [InlineData("fake-a")]
    [InlineData("fake-a", "fake-a")]
    public void Fewer_than_two_distinct_models_is_a_validation_error(params string[] models)
    {
        Assert.Throws<InvalidPlanException>(() => BroadcastPlanner.Plan(Request(models), NoMultipliers));
    }

    [Fact]
    public void Blank_model_id_is_a_validation_error()
    {
        Assert.Throws<InvalidPlanException>(() => BroadcastPlanner.Plan(Request(["fake-a", " "]), NoMultipliers));
    }

    [Fact]
    public void Negative_pacing_is_a_validation_error()
    {
        Assert.Throws<InvalidPlanException>(() => BroadcastPlanner.Plan(
            Request(["fake-a", "fake-b"], pacing: new PacingProfile(-1, 20)), NoMultipliers));
    }

    [Fact]
    public void Window_too_short_for_one_game_is_a_validation_error()
    {
        // One game takes 80 plies × 50 min = 4 000 min, far longer than the one-hour window.
        Assert.Throws<InvalidPlanException>(() => BroadcastPlanner.Plan(
            Request(["fake-a", "fake-b"], durationHours: 1, assumedMoveLatencyMs: 3_000_000), NoMultipliers));
    }
}
