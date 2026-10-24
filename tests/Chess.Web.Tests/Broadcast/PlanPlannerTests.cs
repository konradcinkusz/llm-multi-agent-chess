using Chess.Web.Api;
using Chess.Web.Components.Pages.Broadcast;
using Xunit;
using ApiBroadcast = Chess.Api.Broadcast;

namespace Chess.Web.Tests.Broadcast;

public class PlanPlannerTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 24, 0, 0, 0, TimeSpan.Zero);

    private static readonly IReadOnlyDictionary<string, double?> Multipliers = new Dictionary<string, double?>(StringComparer.Ordinal)
    {
        ["m0"] = 1,
        ["m1"] = 1.5,
        ["m2"] = 2,
        ["m3"] = 0.5,
        ["m4"] = 1,
        ["random"] = null,
    };

    /// <summary>The page's planner must give the slots and estimate the API's planner gives for the same request, so the preview is what the save stores.</summary>
    [Fact]
    public void MatchesTheApiPlannerForEveryRequestInTheMatrix()
    {
        var checkedRequests = 0;
        foreach (var modelCount in new[] { 2, 3, 4, 5 })
        foreach (var seed in new[] { 0, 1, 7, 42 })
        foreach (var hours in new[] { 1, 5, 24, 48 })
        foreach (var (moveDelay, gap) in new[] { (4000, 20), (0, 0), (1500, 5) })
        foreach (var latency in new double[] { 40_000, 2_000 })
        {
            var models = Enumerable.Range(0, modelCount).Select(index => $"m{index}").ToArray();
            var request = new PlanRequest("Day", models, Start, hours, new PacingProfile(moveDelay, gap), seed, 80, latency);
            var apiRequest = new ApiBroadcast.PlanRequest("Day", models, Start, hours,
                new ApiBroadcast.PacingProfile(moveDelay, gap), seed, 80, latency);

            var expected = ApiBroadcast.BroadcastPlanner.Plan(apiRequest,
                new Dictionary<string, double?>(Multipliers, StringComparer.Ordinal));
            var actual = PlanPlanner.Plan(request);

            Assert.Equal(expected.Slots.Count, actual.Count);
            for (var index = 0; index < actual.Count; index++)
            {
                var slot = expected.Slots[index];
                Assert.Equal(slot.White.ModelId, actual[index].White.ModelId);
                Assert.Equal(slot.Black.ModelId, actual[index].Black.ModelId);
                Assert.Equal(slot.White.Kind, actual[index].White.Kind);
                Assert.Equal(slot.PlannedStart, actual[index].PlannedStart);
                Assert.Equal(slot.PlannedEnd, actual[index].PlannedEnd);
            }

            foreach (var estimate in expected.EstimatedPremiumRequests)
            {
                Assert.Equal(estimate.EstimatedPremiumRequests,
                    PlanPlanner.Requests(estimate.ModelId, actual, 80, Multipliers));
            }

            checkedRequests++;
        }

        Assert.Equal(4 * 4 * 4 * 3 * 2, checkedRequests);
    }

    [Fact]
    public void TheSameRequestGivesTheSameSlotsAndADifferentSeedGivesADifferentOrder()
    {
        var models = new[] { "m0", "m1", "m2" };
        var first = PlanPlanner.Plan(Request(models, seed: 3));
        var again = PlanPlanner.Plan(Request(models, seed: 3));
        var other = PlanPlanner.Plan(Request(models, seed: 4));

        Assert.Equal(first, again);
        Assert.NotEqual(first.Select(slot => (slot.White.ModelId, slot.Black.ModelId)), other.Select(slot => (slot.White.ModelId, slot.Black.ModelId)));
    }

    [Fact]
    public void AWindowThatFitsOneGameIsValidAndOneThatDoesNotIsReported()
    {
        Assert.Empty(PlanPlanner.Problems(Request(["m0", "m1"], hours: 24)));

        var problems = PlanPlanner.Problems(Request(["m0", "m1"], hours: 1, moveDelayMs: 4000, latency: 200_000));

        Assert.Equal(["The window is too short for one game at the assumed pace."], problems);
    }

    [Fact]
    public void FewerThanTwoModelsAndOutOfRangeHoursAreReportedBeforeAnyPlanning()
    {
        Assert.Contains("Choose at least two models.", PlanPlanner.Problems(Request(["m0"], hours: 24)));
        Assert.Contains("Choose at least two models.", PlanPlanner.Problems(Request(["m0", "m0"], hours: 24)));
        Assert.Contains($"Hours must be between 1 and {PlanPlanner.MaxDurationHours}.", PlanPlanner.Problems(Request(["m0", "m1"], hours: 0)));
        Assert.Contains($"Hours must be between 1 and {PlanPlanner.MaxDurationHours}.",
            PlanPlanner.Problems(Request(["m0", "m1"], hours: PlanPlanner.MaxDurationHours + 1)));
    }

    [Fact]
    public void ANamelessOrNegativelyPacedRequestIsReported()
    {
        Assert.Contains("Give the schedule a name.", PlanPlanner.Problems(Request(["m0", "m1"]) with { Name = "  " }));
        Assert.Contains("Pacing values must not be negative.",
            PlanPlanner.Problems(Request(["m0", "m1"], moveDelayMs: -1)));
    }

    [Fact]
    public void TheRandomPlayerCostsNothingAndAnUnknownMultiplierGivesNoEstimate()
    {
        var slots = PlanPlanner.Plan(Request(["m0", "random", "m3"]));

        Assert.Equal(0, PlanPlanner.Requests("random", slots, 80, Multipliers));
        Assert.Null(PlanPlanner.Requests("nobody", slots, 80, Multipliers));
    }

    [Fact]
    public void AModelsRequestsAreItsGamesTimesHalfItsPliesTimesItsMultiplier()
    {
        var slots = PlanPlanner.Plan(Request(["m0", "m1"], hours: 2));

        // Two games, both between m0 and m1: 2 × 40 × 1 for m0 and 2 × 40 × 1.5 for m1.
        Assert.Equal(2, slots.Count);
        Assert.Equal(80, PlanPlanner.Requests("m0", slots, 80, Multipliers));
        Assert.Equal(120, PlanPlanner.Requests("m1", slots, 80, Multipliers));
    }

    private static PlanRequest Request(IReadOnlyList<string> models, int hours = 24, int moveDelayMs = 4000,
        double latency = 40_000, int seed = 1) =>
        new("Day", models, Start, hours, new PacingProfile(moveDelayMs, 20), seed, 80, latency);
}
