using System.Net;
using Chess.Web.Api;
using Chess.Web.Tests.Support;
using Xunit;

namespace Chess.Web.Tests.Api;

public class ArenaApiClientTests
{
    private static readonly Guid GameId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    [Fact]
    public async Task GetStatusReadsApiStatus()
    {
        var handler = new FakeHandler(_ => Canned.Json("""{"copilot":"Ready","copilotDetail":"signed in","database":"ok","stockfish":"absent"}"""));
        var api = new ArenaApiClient(Canned.Client(handler));

        var status = await api.GetStatusAsync();

        Assert.Equal("GET", Assert.Single(handler.Requests).Method);
        Assert.Equal("/api/status", handler.Requests[0].Target);
        Assert.Equal(new StatusDto("Ready", "signed in", "ok", "absent"), status);
    }

    [Fact]
    public async Task GetGamesSendsOnlyTheParametersThatAreSet()
    {
        var handler = new FakeHandler(_ => Canned.Json("""{"items":[],"page":2,"pageSize":10,"total":0}"""));
        var api = new ArenaApiClient(Canned.Client(handler));

        await api.GetGamesAsync(2, 10, "gpt-5 mini", null);

        Assert.Equal("/api/games?page=2&pageSize=10&model=gpt-5%20mini", handler.Requests[0].Target);
    }

    [Fact]
    public async Task CreateGamePostsCamelCaseJsonAndReturnsTheSummary()
    {
        var handler = new FakeHandler(_ => Canned.Json(
            $$"""{"id":"{{GameId}}","tournamentId":null,"white":{"kind":"llm","modelId":"m1"},"black":{"kind":"random","modelId":"random"},"rated":true,"state":"queued","result":"*","termination":null,"plyCount":0,"createdAt":"2026-10-24T10:00:00Z","finishedAt":null}"""));
        var api = new ArenaApiClient(Canned.Client(handler));

        var summary = await api.CreateGameAsync(new CreateGameRequest(new PlayerSpec("llm", "m1"), new PlayerSpec("random", "random"), true));

        var request = Assert.Single(handler.Requests);
        Assert.Equal("POST", request.Method);
        Assert.Equal("/api/games", request.Target);
        Assert.Contains("\"white\":{\"kind\":\"llm\",\"modelId\":\"m1\"}", request.Body!);
        Assert.Contains("\"rated\":true", request.Body!);
        Assert.Equal(GameId, summary.Id);
        Assert.Equal("queued", summary.State);
    }

    [Fact]
    public async Task AbortGameSendsDelete()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var api = new ArenaApiClient(Canned.Client(handler));

        await api.AbortGameAsync(GameId);

        Assert.Equal("DELETE", handler.Requests[0].Method);
        Assert.Equal($"/api/games/{GameId}", handler.Requests[0].Target);
    }

    [Fact]
    public async Task GetPgnReturnsThePlainText()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("[Event \"Arena\"]\n\n*\n")
        });
        var api = new ArenaApiClient(Canned.Client(handler));

        var pgn = await api.GetGamePgnAsync(GameId);

        Assert.Equal($"/api/games/{GameId}/pgn", handler.Requests[0].Target);
        Assert.Equal("[Event \"Arena\"]\n\n*\n", pgn);
    }

    [Fact]
    public async Task RatingHistoryEncodesTheModelId()
    {
        var handler = new FakeHandler(_ => Canned.Json("[]"));
        var api = new ArenaApiClient(Canned.Client(handler));

        await api.GetRatingHistoryAsync("a b&c");

        Assert.Equal("/api/ratings/history?model=a%20b%26c", handler.Requests[0].Target);
    }

    [Fact]
    public async Task ResultsBreakdownWithoutATournamentHasNoQuery()
    {
        var handler = new FakeHandler(_ => Canned.Json("[]"));
        var api = new ArenaApiClient(Canned.Client(handler));

        await api.GetResultsBreakdownAsync(null);

        Assert.Equal("/api/results/breakdown", handler.Requests[0].Target);
    }

    [Fact]
    public async Task AttemptsSendTheModelAndPage()
    {
        var handler = new FakeHandler(_ => Canned.Json("""{"items":[],"page":3,"pageSize":50,"total":0}"""));
        var api = new ArenaApiClient(Canned.Client(handler));

        await api.GetAttemptsAsync(null, "m1", 3);

        Assert.Equal("/api/attempts?model=m1&page=3", handler.Requests[0].Target);
    }

    [Fact]
    public async Task AServerErrorIsNotSwallowed()
    {
        var handler = new FakeHandler(_ => Canned.Json("{}", HttpStatusCode.ServiceUnavailable));
        var api = new ArenaApiClient(Canned.Client(handler));

        await Assert.ThrowsAsync<HttpRequestException>(() => api.GetStatusAsync());
    }
}
