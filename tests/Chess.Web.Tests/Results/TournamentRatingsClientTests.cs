using Chess.Web.Api;
using Chess.Web.Components.Pages.Results;
using Chess.Web.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Chess.Web.Tests.Results;

public class TournamentRatingsClientTests
{
    private static readonly Guid TournamentId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private const string PairJson =
        """{"a":"alpha","b":"beta","expectedScore":0.6,"qualityShare":0.5,"rqc":0.6,"rqcLow":0.5,"rqcHigh":0.7,"eloEquivalent":70,"headToHeadGames":0,"headToHeadScore":null}""";

    [Fact]
    public void ClientNameResolvesToTheTypedArenaApiRegistration()
    {
        // Program.cs registers ArenaApiClient with AddHttpClient<ArenaApiClient>; the adapter reuses that
        // configuration by name, so the name must match the registration or the adapter would call without a base address.
        var services = new ServiceCollection();
        services.AddHttpClient<ArenaApiClient>(client => client.BaseAddress = new Uri("http://api"));
        var factory = services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();

        var client = factory.CreateClient(TournamentRatingsClient.ClientName);

        Assert.Equal(new Uri("http://api"), client.BaseAddress);
    }

    [Fact]
    public async Task AllGamesSendsNoTournamentFilter()
    {
        var handler = new FakeHandler(_ => Canned.Json("""{"models":[],"whiteAdvantage":0,"games":0,"moveQualityAvailable":false}"""));
        var ratings = new TournamentRatingsClient(Canned.Client(handler));

        await ratings.GetLeaderboardAsync(null);

        Assert.Equal("/api/ratings", Assert.Single(handler.Requests).Target);
    }

    [Fact]
    public async Task ALeaderboardOfOneTournamentSendsItsId()
    {
        var handler = new FakeHandler(_ => Canned.Json("""{"models":[],"whiteAdvantage":0,"games":0,"moveQualityAvailable":false}"""));
        var ratings = new TournamentRatingsClient(Canned.Client(handler));

        await ratings.GetLeaderboardAsync(TournamentId);

        Assert.Equal($"/api/ratings?tournamentId={TournamentId}", Assert.Single(handler.Requests).Target);
    }

    [Fact]
    public async Task PairwiseEscapesModelIdsAndKeepsTheTournamentFilter()
    {
        var handler = new FakeHandler(_ => Canned.Json(PairJson));
        var ratings = new TournamentRatingsClient(Canned.Client(handler));

        var pair = await ratings.GetPairwiseAsync("gpt 5/mini", "beta", TournamentId);

        Assert.Equal($"/api/ratings/pairwise?a=gpt%205%2Fmini&b=beta&tournamentId={TournamentId}", Assert.Single(handler.Requests).Target);
        Assert.Equal(0.5, pair.RqcLow);
    }

    [Fact]
    public async Task MatrixSendsTheTournamentFilterWhenThereIsOne()
    {
        var handler = new FakeHandler(_ => Canned.Json("""{"models":[],"cells":[]}"""));
        var ratings = new TournamentRatingsClient(Canned.Client(handler));

        await ratings.GetRatingMatrixAsync(TournamentId);

        Assert.Equal($"/api/ratings/matrix?tournamentId={TournamentId}", Assert.Single(handler.Requests).Target);
    }
}
