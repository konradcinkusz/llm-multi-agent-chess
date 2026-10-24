using System.Net;
using Chess.Web.Api;
using Chess.Web.Components.Pages.Tournaments;
using Chess.Web.Tests.Support;
using Xunit;

namespace Chess.Web.Tests.Tournaments;

public class TournamentGamesTests
{
    private static readonly Guid TournamentId = Guid.Parse(Fixtures.RunningId);
    private const string OtherTournament = "33333333-3333-3333-3333-333333333333";

    [Fact]
    public async Task ReadsEveryPageAndKeepsOnlyTheGamesOfThisTournament()
    {
        // The tournament's game sits on the second page, after a full page of other games.
        var first = Enumerable.Repeat(Fixtures.Game("aaaaaaaa-0000-0000-0000-000000000000", OtherTournament, "gpt", "claude", "1-0", 41, "2026-10-24T09:00:00Z"), 200);
        var second = Fixtures.Game("bbbbbbbb-0000-0000-0000-000000000000", Fixtures.RunningId, "claude", "gpt", "0-1", 30, "2026-10-24T10:30:00Z");
        var handler = Serve.Routes(new Dictionary<string, Reply>
        {
            ["GET /api/games?page=1&pageSize=200"] = Serve.Ok(Fixtures.Page(first, 201)),
            ["GET /api/games?page=2&pageSize=200"] = Serve.Ok(Fixtures.Page([second], 201)),
        });
        var api = new ArenaApiClient(Canned.Client(handler));

        var games = await TournamentGames.LoadAsync(api, TournamentId, default);

        var game = Assert.Single(games);
        Assert.Equal(Guid.Parse("bbbbbbbb-0000-0000-0000-000000000000"), game.Id);
        Assert.Equal(new[] { "/api/games?page=1&pageSize=200", "/api/games?page=2&pageSize=200" },
            handler.Requests.Select(r => r.Target).ToArray());
    }

    [Fact]
    public async Task StopsAtTheFirstEmptyPageEvenWhenTheTotalSaysMore()
    {
        var handler = Serve.Routes(new Dictionary<string, Reply>
        {
            ["GET /api/games?page=1&pageSize=200"] = Serve.Ok(Fixtures.Page([], 500)),
        });
        var api = new ArenaApiClient(Canned.Client(handler));

        var games = await TournamentGames.LoadAsync(api, TournamentId, default);

        Assert.Empty(games);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ReturnsTheGamesOldestFirst()
    {
        var later = Fixtures.Game("cccccccc-0000-0000-0000-000000000000", Fixtures.RunningId, "gpt", "claude", "1-0", 20, "2026-10-24T12:00:00Z");
        var earlier = Fixtures.Game("dddddddd-0000-0000-0000-000000000000", Fixtures.RunningId, "claude", "gpt", "0-1", 20, "2026-10-24T08:00:00Z");
        var handler = Serve.Routes(new Dictionary<string, Reply>
        {
            ["GET /api/games?page=1&pageSize=200"] = Serve.Ok(Fixtures.Page([later, earlier], 2)),
        });
        var api = new ArenaApiClient(Canned.Client(handler));

        var games = await TournamentGames.LoadAsync(api, TournamentId, default);

        Assert.Equal(
            [Guid.Parse("dddddddd-0000-0000-0000-000000000000"), Guid.Parse("cccccccc-0000-0000-0000-000000000000")],
            games.Select(game => game.Id).ToArray());
    }
}
