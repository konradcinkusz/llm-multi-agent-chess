using System.Text.Json;
using Chess.Web.Api;
using Chess.Web.Components.Pages.Play;
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
        Assert.Equal(new ThinkingState(1, "White", "gpt"), game.Thinking);

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
