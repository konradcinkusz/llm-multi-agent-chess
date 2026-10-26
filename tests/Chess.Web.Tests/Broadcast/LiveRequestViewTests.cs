using Chess.Web.Api;
using Chess.Web.Components.Pages.Broadcast;
using Chess.Web.Components.Pages.Play;
using Xunit;

namespace Chess.Web.Tests.Broadcast;

public class LiveRequestViewTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 24, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AQueueWithNothingPlayingOrWaitingIsNotWaiting()
    {
        Assert.False(LiveRequestView.HasWaiting(null));
        Assert.False(LiveRequestView.HasWaiting(Queue()));
        Assert.True(LiveRequestView.HasWaiting(Queue(queued: [Request("Ana", "gpt", "claude", "queued", 1)])));
        Assert.True(LiveRequestView.HasWaiting(Queue(playing: Request("Ana", "gpt", "claude", "playing"))));
    }

    [Fact]
    public void TheRequesterIsNamedOnlyWhenTheGameOnAirIsTheRequest()
    {
        var game = Guid.NewGuid();
        var queue = Queue(playing: Request("Ana", "gpt", "claude", "playing", gameId: game));

        Assert.Equal("Ana", LiveRequestView.RequestedBy(queue, game));
        Assert.Null(LiveRequestView.RequestedBy(queue, Guid.NewGuid()));
        Assert.Null(LiveRequestView.RequestedBy(null, game));
    }

    [Fact]
    public void RejectionsNameThePayerAndTheMenuAndNeverShowTheReason()
    {
        var queue = Queue(recent: [
                Request("Bo", "gpt", "claude", "rejected", reason: "price above the largest tier"),
                Request("Cy", "gpt", "claude", "played"),
                Request("Di", "gpt", "claude", "rejected"),
                Request("Ed", "gpt", "claude", "rejected"),
            ]);

        var lines = LiveRequestView.RejectionLines(queue);

        Assert.Equal(["@Bo: credit kept, see the menu", "@Di: credit kept, see the menu"], lines);
        Assert.DoesNotContain(lines, line => line.Contains("tier", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData(0, "starts next")]
    [InlineData(1, "about 1 min")]
    [InlineData(12, "about 12 min")]
    public void TheWaitIsSaidInWords(int? minutes, string expected) =>
        Assert.Equal(expected, LiveRequestView.WaitLabel(minutes));

    [Fact]
    public void APriceIsShownToTheCent()
    {
        Assert.Equal("$1.00", LiveRequestView.Price(1));
        Assert.Equal("$0.40", LiveRequestView.Price(0.4));
    }

    [Fact]
    public void TheReplayIsTheBestRatedRecentGameAndTheNewerOneWinsATie()
    {
        var board = Board(("alpha", 1600), ("beta", 1500), ("gamma", 1400), ("delta", 1300));
        var strong = Game("alpha", "beta", at: 0);
        var same = Game("alpha", "beta", at: 1);
        var weak = Game("gamma", "delta", at: 2);

        var pick = ReplayPicker.Pick([strong, same, weak], board, avoid: null);

        Assert.Equal(strong.Id, pick!.Id);
        Assert.Equal(same.Id, ReplayPicker.Pick([same, strong, weak], board, avoid: strong.Id)!.Id);
    }

    [Fact]
    public void AGameIsNotReplayedAgainWhileAnotherIsAvailable()
    {
        var board = Board(("alpha", 1600), ("beta", 1500));
        var first = Game("alpha", "beta", at: 0);
        var second = Game("alpha", "beta", at: 1);

        Assert.Equal(second.Id, ReplayPicker.Pick([first, second], board, avoid: first.Id)!.Id);
    }

    [Fact]
    public void TheOnlyStoredGameIsReplayedAgainWhenItIsTheLastOne()
    {
        var only = Game("alpha", "beta", at: 0);

        Assert.Equal(only.Id, ReplayPicker.Pick([only], null, avoid: only.Id)!.Id);
    }

    [Fact]
    public void UnfinishedAndUnratedGamesAreNotReplayed()
    {
        var running = Game("alpha", "beta", at: 0) with { State = "running" };
        var unrated = Game("alpha", "beta", at: 1) with { Rated = false };

        Assert.Null(ReplayPicker.Pick([running, unrated], null, avoid: null));
        Assert.Null(ReplayPicker.Pick([], null, avoid: null));
    }

    [Fact]
    public void ThePositionAfterSomePliesIsTheFenOfThatMove()
    {
        var game = Detail(
            new MoveDto(1, "white", "e4", "FEN-ONE", 1, 1, null, null, null, null),
            new MoveDto(2, "black", "e5", "FEN-TWO", 1, 1, null, null, null, null));

        var none = ReplayPicker.UpTo(game, 0);
        var one = ReplayPicker.UpTo(game, 1);

        Assert.Empty(none.Moves);
        Assert.Equal(game.StartFen, none.CurrentFen);
        Assert.Single(one.Moves);
        Assert.Equal("FEN-ONE", one.CurrentFen);
    }

    private static RequestQueueDto Queue(GameRequestDto? playing = null, GameRequestDto[]? queued = null,
        GameRequestDto[]? recent = null) => new(playing, queued ?? [], recent ?? [], null);

    private static GameRequestDto Request(string payer, string white, string black, string status, int? position = null,
        Guid? gameId = null, string? reason = null) =>
        new(Guid.NewGuid(), "youtube", payer, white, black, 1, status, reason, gameId, At, position);

    private static LeaderboardDto Board(params (string Model, double Rating)[] models) =>
        new(models.Select(m => new ModelRating(m.Model, m.Rating, 0, 0, 10, 0.5, 1, null, 0.5, 50, false)).ToList(), 0, 10, false);

    private static GameSummaryDto Game(string white, string black, int at) => new(Guid.NewGuid(), null,
        new PlayerSpec("llm", white), new PlayerSpec("llm", black), true, "finished", "1-0", "checkmate", 40,
        At.AddMinutes(at), At.AddMinutes(at + 1));

    private static GameDetailDto Detail(params MoveDto[] moves) =>
        new(Game("alpha", "beta", at: 0), LiveGame.StartFen, LiveGame.StartFen, moves);
}
