using System.Text.Json;
using Chess.Web.Api;
using Chess.Web.Components.Pages.Play;
using Chess.Web.Components.Shared;
using Chess.Web.Components.Shared.Game;
using Xunit;

namespace Chess.Web.Tests.Play;

public class LiveGameTests
{
    [Fact]
    public void AMovePlayedAppendsTheMoveAndItsPosition()
    {
        var game = Started();

        var result = game.Apply(Event("MovePlayed", PlayFixtures.MovePlayed(1, "White", "e4", PlayFixtures.AfterE4)));

        Assert.Equal(ApplyResult.Applied, result);
        Assert.Equal(["e4"], game.Moves.Select(move => move.San));
        Assert.Equal(PlayFixtures.AfterE4, game.CurrentFen);
        Assert.Equal(1, game.AppliedPly);
    }

    [Fact]
    public void EventsForAPlyTheGameAlreadyHasAreDuplicatesAndChangeNothing()
    {
        var game = Started(plies: [("White", "e4", PlayFixtures.AfterE4)]);

        Assert.Equal(ApplyResult.Duplicate, game.Apply(Event("MovePlayed", PlayFixtures.MovePlayed(1, "White", "e4", PlayFixtures.AfterE4))));
        Assert.Equal(ApplyResult.Duplicate, game.Apply(Event("MoveStarted", PlayFixtures.MoveStarted(1, "White", "gpt"))));
        Assert.Equal(ApplyResult.Duplicate, game.Apply(Event("AttemptRejected", PlayFixtures.AttemptRejected(1, 1, "Ke9", "illegal"))));

        Assert.Single(game.Moves);
        Assert.Null(game.Thinking);
        Assert.Empty(game.Notices);
    }

    [Fact]
    public void APlyAfterAMissingOneIsAGapAndIsNotApplied()
    {
        var game = Started();

        var result = game.Apply(Event("MovePlayed", PlayFixtures.MovePlayed(2, "Black", "e5", PlayFixtures.AfterE5)));

        Assert.Equal(ApplyResult.Gap, result);
        Assert.Empty(game.Moves);
        Assert.Equal(0, game.AppliedPly);
    }

    [Fact]
    public void MoveStartedSetsTheThinkingSideAndMovePlayedClearsIt()
    {
        var game = Started();

        game.Apply(Event("MoveStarted", PlayFixtures.MoveStarted(1, "White", "gpt")));
        Assert.Equal(new ThinkingState(1, "White", "gpt", DateTimeOffset.Parse("2026-10-24T10:00:00Z"), 1), game.Thinking);

        game.Apply(Event("MovePlayed", PlayFixtures.MovePlayed(1, "White", "e4", PlayFixtures.AfterE4)));
        Assert.Null(game.Thinking);
    }

    [Fact]
    public void AnAttemptRejectedIsKeptWithTheColourOfItsPly()
    {
        var game = Started(plies: [("White", "e4", PlayFixtures.AfterE4)]);

        game.Apply(Event("AttemptRejected", PlayFixtures.AttemptRejected(2, 1, "Ke9", "Ke9 is not a legal move")));

        var notice = Assert.Single(game.Notices);
        Assert.Equal(new AttemptNotice(2, "Black", 1, "Ke9 is not a legal move", "Ke9"), notice);
    }

    [Fact]
    public void GameFinishedSetsTheOutcomeAndEndsTheThinking()
    {
        var game = Started();
        game.Apply(Event("MoveStarted", PlayFixtures.MoveStarted(1, "White", "gpt")));

        game.Apply(Event("GameFinished", PlayFixtures.GameFinished("0-1", "forfeit-white")));

        Assert.True(game.IsFinished);
        Assert.Equal(new GameOutcome("0-1", "forfeit-white"), game.Outcome);
        Assert.Null(game.Thinking);
    }

    [Fact]
    public void ALoadedGameTakesTheServersStateAndItsFinishedOutcome()
    {
        var game = Started();

        game.Load(Detail("finished", "1-0", "checkmate", ("White", "e4", PlayFixtures.AfterE4), ("Black", "e5", PlayFixtures.AfterE5)));

        Assert.Equal(2, game.AppliedPly);
        Assert.Equal(["e4", "e5"], game.Moves.Select(move => move.San));
        Assert.Equal(PlayFixtures.AfterE5, game.CurrentFen);
        Assert.Equal(new GameOutcome("1-0", "checkmate"), game.Outcome);
    }

    [Fact]
    public void ALoadOlderThanTheEventsAlreadyAppliedIsIgnored()
    {
        var game = Started(plies: [("White", "e4", PlayFixtures.AfterE4), ("Black", "e5", PlayFixtures.AfterE5)]);

        game.Load(Detail("running", "*", null, ("White", "e4", PlayFixtures.AfterE4)));

        Assert.Equal(2, game.AppliedPly);
        Assert.Equal(2, game.Moves.Count);
    }

    [Fact]
    public void AnUnknownEventTypeIsNotApplied()
    {
        var game = Started();

        Assert.Equal(ApplyResult.Unknown, game.Apply(Event("SomethingNew", "{}")));
    }

    [Fact]
    public void ARejectedAttemptIsLoggedAndTheModelMovesOnToItsNextAttempt()
    {
        var game = Started();
        game.Apply(Event("MoveStarted", PlayFixtures.MoveStarted(1, "White", "gpt")));

        game.Apply(Event("AttemptRecorded", PlayFixtures.AttemptRecorded(1, 1, "White", "gpt", "Ke9", legal: false,
            rejectionKind: "illegal", rawReply: "Ke9")));

        Assert.Equal(2, game.Thinking!.Attempt);
        var entry = Assert.Single(game.Attempts);
        Assert.Equal((1, 1, "White", false, "illegal"), (entry.Ply, entry.Attempt, entry.Color, entry.Legal, entry.RejectionKind));
        Assert.Equal("Ke9", entry.RawReply);
    }

    [Fact]
    public void ALegalAttemptIsLoggedAndTheMoveThenEndsWithMovePlayed()
    {
        var game = Started();
        game.Apply(Event("MoveStarted", PlayFixtures.MoveStarted(1, "White", "gpt")));

        game.Apply(Event("AttemptRecorded", PlayFixtures.AttemptRecorded(1, 1, "White", "gpt", "e4", legal: true,
            rejectionKind: null, rawReply: "e4", reasoning: "Open the centre.", aiCredits: 0.5)));

        Assert.Equal(1, game.Thinking!.Attempt);
        Assert.True(game.Attempts[0].Legal);
        Assert.Equal("Open the centre.", game.Attempts[0].Reasoning);
        Assert.Equal(0.5, game.Attempts[0].AiCredits);
    }

    [Fact]
    public void TheSameAttemptFromTheLoadAndFromTheStreamIsOneEntryAndAttemptsAreInPlyOrder()
    {
        var game = Started(plies: [("White", "e4", PlayFixtures.AfterE4)]);
        var loaded = new EngineEntry(2, 1, "claude", "prompt", "Ke9", null, null, false, "illegal", 800, 100, 10, null, null, null);
        game.AddAttempts([loaded, new EngineEntry(1, 1, "gpt", "prompt", "e4", null, "e4", true, null, 700, 100, 10, null, null, null)]);

        game.Apply(Event("AttemptRecorded", PlayFixtures.AttemptRecorded(2, 1, "Black", "claude", "Ke9", legal: false,
            rejectionKind: "illegal", rawReply: "Ke9")));

        Assert.Equal(new[] { (1, 1), (2, 1) }, game.Attempts.Select(entry => (entry.Ply, entry.Attempt)));
    }

    [Fact]
    public void TheLastMoveAndTheCheckedKingComeFromTheMovesOnTheBoard()
    {
        var game = Started(plies: [("White", "e4", PlayFixtures.AfterE4)]);

        Assert.Equal(new BoardMove("e2", "e4"), game.LastMove);
        Assert.Null(game.CheckSquare);
    }

    [Fact]
    public void TheSideIsTakenFromThePlyBecauseTheApiSendsColoursInLowerCase()
    {
        var game = Started();

        game.Apply(Event("MoveStarted", PlayFixtures.MoveStarted(2, "black", "claude")));

        Assert.Equal(new ThinkingState(2, "Black", "claude", DateTimeOffset.Parse("2026-10-24T10:00:00Z"), 1), game.Thinking);
        game.Apply(Event("MovePlayed", PlayFixtures.MovePlayed(1, "white", "e4", PlayFixtures.AfterE4)));
        Assert.Equal("White", game.Moves[0].Color);
    }

    [Fact]
    public void AGameThatSendsEventsIsRunningNotQueued()
    {
        var queued = Detail("queued", "*", null);
        var game = new LiveGame(queued.Summary);
        game.Load(queued);
        Assert.Equal("queued", game.Summary.State);

        game.Apply(Event("MoveStarted", PlayFixtures.MoveStarted(1, "White", "gpt")));

        Assert.Equal("running", game.Summary.State);
    }

    [Fact]
    public void ANewGameHasNoLastMoveAndNoAttempts()
    {
        var game = Started();

        Assert.Null(game.LastMove);
        Assert.Empty(game.Attempts);
        Assert.Equal(1, game.NextPly);
    }

    private static LiveGame Started(params (string Color, string San, string Fen)[] plies)
    {
        var detail = Detail("running", "*", null, plies);
        var game = new LiveGame(detail.Summary);
        game.Load(detail);
        return game;
    }

    private static GameDetailDto Detail(string state, string result, string? termination, params (string Color, string San, string Fen)[] plies)
    {
        var json = PlayFixtures.Detail(state, result, termination, plies);
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Deserialize<GameDetailDto>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    private static GameEventMessage Event(string type, string json)
    {
        using var document = JsonDocument.Parse(json);
        return new GameEventMessage(type, document.RootElement.Clone());
    }
}
