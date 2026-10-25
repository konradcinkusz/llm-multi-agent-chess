using System.Net;
using Bunit;
using Chess.Web.Components.Pages.Tournaments;
using Chess.Web.Tests.Support;
using Xunit;

namespace Chess.Web.Tests.Tournaments;

public class TournamentDetailPageTests
{
    private const string OtherGameId = "44444444-4444-4444-4444-444444444444";

    private const string Leaderboard =
        """{"models":[{"model":"claude","rating":1480,"ciLow":1440,"ciHigh":1520,"games":2,"score":0.4,"legality":1,"moveQuality":null,"quality":0.4,"arenaScore":0.4,"provisional":true},{"model":"gpt","rating":1520,"ciLow":1480,"ciHigh":1560,"games":2,"score":0.6,"legality":0.95,"moveQuality":null,"quality":0.6,"arenaScore":0.6,"provisional":true}],"whiteAdvantage":0.5,"games":2,"moveQualityAvailable":false}""";

    [Fact]
    public void ShowsOnlyThisTournamentsGamesAndRanksItsLeaderboardByArenaScore()
    {
        var handler = Serve.Routes(new Dictionary<string, Reply>
        {
            [$"GET /api/tournaments/{Fixtures.FinishedId}"] = Serve.Ok(Fixtures.FinishedTournament),
            ["GET /api/games?page=1&pageSize=200"] = Serve.Ok(Fixtures.Page(
            [
                Fixtures.Game("55555555-5555-5555-5555-555555555555", Fixtures.FinishedId, "gpt", "claude", "1-0", 41, "2026-09-01T10:00:00Z"),
                Fixtures.Game(OtherGameId, null, "gpt", "claude", "0-1", 30, "2026-09-01T11:00:00Z"),
                Fixtures.Game("66666666-6666-6666-6666-666666666666", Fixtures.FinishedId, "claude", "gpt", "1/2-1/2", 60, "2026-09-01T12:00:00Z"),
            ], 3)),
            [$"GET /api/ratings?tournamentId={Fixtures.FinishedId}"] = Serve.Ok(Leaderboard),
        });
        using var ctx = new BunitContext();
        Wire.Api(ctx, handler);

        var cut = ctx.Render<TournamentDetailPage>(parameters => parameters.Add(page => page.Id, Guid.Parse(Fixtures.FinishedId)));

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("#tournament-leaderboard tbody tr").Count));
        var games = cut.FindAll("#tournament-games tbody tr");
        Assert.Equal(2, games.Count);
        Assert.DoesNotContain(OtherGameId, cut.Markup);
        Assert.Equal("/games/55555555-5555-5555-5555-555555555555", games[0].QuerySelector("a")!.GetAttribute("href"));

        var ranked = cut.FindAll("#tournament-leaderboard tbody tr");
        Assert.Equal("gpt", ranked[0].Children[1].TextContent);
        Assert.Equal("claude", ranked[1].Children[1].TextContent);
        Assert.Contains($"/api/ratings?tournamentId={Fixtures.FinishedId}", handler.Requests.Select(r => r.Target));
        Assert.DoesNotContain(handler.Requests, request => request.Method == "DELETE");
    }

    [Fact]
    public void SaysSoWhenTheTournamentDoesNotExist()
    {
        var handler = Serve.Routes(new Dictionary<string, Reply>
        {
            [$"GET /api/tournaments/{Fixtures.FinishedId}"] = Serve.Fail(HttpStatusCode.NotFound),
        });
        using var ctx = new BunitContext();
        Wire.Api(ctx, handler);

        var cut = ctx.Render<TournamentDetailPage>(parameters => parameters.Add(page => page.Id, Guid.Parse(Fixtures.FinishedId)));

        cut.WaitForAssertion(() => Assert.Contains("No tournament has the id", cut.Markup));
        Assert.Empty(cut.FindAll("#tournament-games"));
    }

    [Fact]
    public void ARunningTournamentWithNoRatedGamesSaysSoAndCanBeCancelled()
    {
        var handler = Serve.Routes(new Dictionary<string, Reply>
        {
            [$"GET /api/tournaments/{Fixtures.RunningId}"] = Serve.Ok(Fixtures.RunningTournament),
            ["GET /api/games?page=1&pageSize=200"] = Serve.Ok(Fixtures.Page([], 0)),
            [$"GET /api/ratings?tournamentId={Fixtures.RunningId}"] = Serve.Ok(
                """{"models":[],"whiteAdvantage":0,"games":0,"moveQualityAvailable":false}"""),
        });
        using var ctx = new BunitContext();
        Wire.Api(ctx, handler);

        var cut = ctx.Render<TournamentDetailPage>(parameters => parameters.Add(page => page.Id, Guid.Parse(Fixtures.RunningId)));

        cut.WaitForAssertion(() => Assert.Contains("No rated games have finished yet.", cut.Markup));
        Assert.Contains("No games yet.", cut.Markup);
        Assert.Contains("1 of 2 games", cut.Markup);
        Assert.Contains("Cancel tournament", cut.Markup);
    }

    [Fact]
    public void TheTablesScrollInsideTableWrapsAndTheFactsSitInACard()
    {
        var handler = Serve.Routes(new Dictionary<string, Reply>
        {
            [$"GET /api/tournaments/{Fixtures.FinishedId}"] = Serve.Ok(Fixtures.FinishedTournament),
            ["GET /api/games?page=1&pageSize=200"] = Serve.Ok(Fixtures.Page(
            [
                Fixtures.Game("55555555-5555-5555-5555-555555555555", Fixtures.FinishedId, "gpt", "claude", "1-0", 41, "2026-09-01T10:00:00Z"),
            ], 1)),
            [$"GET /api/ratings?tournamentId={Fixtures.FinishedId}"] = Serve.Ok(Leaderboard),
        });
        using var ctx = new BunitContext();
        Wire.Api(ctx, handler);

        var cut = ctx.Render<TournamentDetailPage>(parameters => parameters.Add(page => page.Id, Guid.Parse(Fixtures.FinishedId)));

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("#tournament-leaderboard tbody tr").Count));
        Assert.NotNull(cut.Find("#tournament-leaderboard").Closest(".table-wrap"));
        Assert.NotNull(cut.Find("#tournament-games").Closest(".table-wrap"));
        Assert.NotNull(cut.Find("dl.tournament-facts").Closest(".card"));
        Assert.Equal(2, cut.FindAll("#tournament-leaderboard .badge-warning").Count);
    }
}
