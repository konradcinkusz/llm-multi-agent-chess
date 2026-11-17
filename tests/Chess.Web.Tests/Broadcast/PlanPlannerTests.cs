using Chess.Web.Api;
using Chess.Web.Components.Pages.Broadcast;
using Xunit;
using ApiBroadcast = Chess.Api.Broadcast;

namespace Chess.Web.Tests.Broadcast;

public class PlanPlannerTests
{
    private static readonly IReadOnlyDictionary<string, double?> Multipliers = new Dictionary<string, double?>(StringComparer.Ordinal)
    {
        ["m0"] = 1,
        ["m1"] = 1.5,
        ["random"] = null,
    };

    [Fact]
    public void TheLimitsMatchTheApisLimits()
    {
        // The Web project may not reference Chess.Api in source, so it holds copies; this keeps the copies honest.
        Assert.Equal(ApiBroadcast.BroadcastEndpoints.MaxDurationHours, PlanPlanner.MaxDurationHours);
        Assert.Equal(ApiBroadcast.BroadcastPlanner.MaxSlots, PlanPlanner.MaxGames);
    }

    [Fact]
    public void TheRandomPlayerCostsNothingAndAnUnknownMultiplierGivesNoEstimate()
    {
        var games = new List<ScheduleGame> { Game("m0", "random") };

        Assert.Equal(0, PlanPlanner.Requests("random", games, 80, Multipliers));
        Assert.Null(PlanPlanner.Requests("nobody", games, 80, Multipliers));
    }

    [Fact]
    public void AModelsRequestsAreItsGamesTimesHalfItsPliesTimesItsMultiplier()
    {
        var games = new List<ScheduleGame> { Game("m0", "m1"), Game("m1", "m0") };

        // Two games, both between m0 and m1: 2 × 40 × 1 for m0 and 2 × 40 × 1.5 for m1.
        Assert.Equal(80, PlanPlanner.Requests("m0", games, 80, Multipliers));
        Assert.Equal(120, PlanPlanner.Requests("m1", games, 80, Multipliers));
    }

    [Fact]
    public void AStyledPlayerIsBilledAsItsModel()
    {
        var games = new List<ScheduleGame> { Game("m0~think", "m1") };

        Assert.Equal(40, PlanPlanner.Requests("m0~think", games, 80, Multipliers));
    }

    private static ScheduleGame Game(string white, string black) => new(
        new PlayerSpec(white == "random" ? "random" : "llm", white),
        new PlayerSpec(black == "random" ? "random" : "llm", black));
}
