using Chess.Api.Players;
using Chess.Engine;
using Xunit;

namespace Chess.Api.Tests.Evals;

/// <summary>
/// Checks the scenario data itself, so a mislabelled scenario fails here rather than passing by accident: each position
/// is legal, each label agrees with <see cref="MoveNotation"/>, and no class is missing from the set.
/// </summary>
public class EvalSuiteTests
{
    private static readonly int MaxAttempts = new PlayerOptions().MaxAttemptsPerMove;

    [Fact]
    public void SuiteHasThirtyPositions()
    {
        Assert.Equal(30, EvalScenarios.All.Count);
    }

    [Fact]
    public void IdsAreUnique()
    {
        var duplicates = EvalScenarios.All.GroupBy(s => s.Id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void EveryClassIsRepresented()
    {
        foreach (var scenarioClass in Enum.GetValues<ScenarioClass>())
        {
            Assert.True(EvalScenarios.All.Any(s => s.Class == scenarioClass), $"No scenario of class {scenarioClass}.");
        }
    }

    [Fact]
    public void EveryPositionHasLegalMoves()
    {
        foreach (var scenario in EvalScenarios.All)
        {
            var board = LoadBoard(scenario);

            Assert.NotEmpty(board.GetLegalMoves());
        }
    }

    [Fact]
    public void EveryLabelAgreesWithTheMoveNotationContract()
    {
        foreach (var scenario in EvalScenarios.All)
        {
            var board = LoadBoard(scenario);
            for (var i = 0; i < scenario.Replies.Count; i++)
            {
                var isLastReply = i == scenario.Replies.Count - 1;
                var expectLegal = isLastReply && scenario.ExpectedSan is not null;
                var resolved = MoveNotation.TryResolve(board, scenario.Replies[i], out var san);

                Assert.True(
                    resolved == expectLegal,
                    $"{scenario.Id}: reply {i + 1} '{scenario.Replies[i]}' resolves={resolved}, label says {expectLegal}.");
                if (expectLegal)
                {
                    Assert.Equal(scenario.ExpectedSan, san);
                }
            }
        }
    }

    [Fact]
    public void ForfeitsUseEveryAllowedAttempt()
    {
        foreach (var scenario in EvalScenarios.All.Where(s => s.ExpectedSan is null))
        {
            Assert.True(
                scenario.Replies.Count == MaxAttempts,
                $"{scenario.Id} has {scenario.Replies.Count} replies; a forfeit needs exactly {MaxAttempts}.");
        }
    }

    [Fact]
    public void ResolvingScenariosNeverExceedTheAttemptLimit()
    {
        foreach (var scenario in EvalScenarios.All.Where(s => s.ExpectedSan is not null))
        {
            Assert.True(
                scenario.Replies.Count <= MaxAttempts,
                $"{scenario.Id} has {scenario.Replies.Count} replies; the limit is {MaxAttempts}.");
        }
    }

    private static ChessBoard LoadBoard(EvalScenario scenario)
    {
        var board = new ChessBoard();
        board.LoadFen(scenario.Fen);
        return board;
    }
}
