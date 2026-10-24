using Chess.Analysis;
using Chess.Api.Quality;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Chess.Api.Tests.Quality;

public class MoveQualityPipelineTests
{
    private const string Start = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
    private static readonly string[] ThreeMoves = ["e4", "e5", "Nf3"];

    private static MoveQualityPipeline Pipeline(string databaseName, IMoveQualityAnalyzer analyzer) =>
        new(new QualityFactory(databaseName), analyzer, NullLogger<MoveQualityPipeline>.Instance);

    [Fact]
    public async Task Stub_analyzer_annotates_every_move_of_a_finished_game()
    {
        var name = Guid.NewGuid().ToString();
        var game = Guid.NewGuid();
        await QualitySeed.AddGameAsync(name, game, Start, ThreeMoves, QualitySeed.T0.AddMinutes(5));

        var annotated = await Pipeline(name, new StubMoveQualityAnalyzer()).AnnotatePendingAsync(CancellationToken.None);

        Assert.Equal(1, annotated);
        var moves = await QualitySeed.LoadMovesAsync(name);
        for (var ply = 1; ply <= 3; ply++)
        {
            var move = moves[(game, ply)];
            Assert.Equal(10 * ply, move.CentipawnLoss);
            Assert.Equal($"best{ply}", move.BestSan);
        }
    }

    [Fact]
    public async Task A_second_pass_finds_nothing_to_do_and_does_not_call_the_analyzer_again()
    {
        var name = Guid.NewGuid().ToString();
        await QualitySeed.AddGameAsync(name, Guid.NewGuid(), Start, ThreeMoves, QualitySeed.T0.AddMinutes(5));
        var analyzer = new StubMoveQualityAnalyzer();
        var pipeline = Pipeline(name, analyzer);

        await pipeline.AnnotatePendingAsync(CancellationToken.None);
        var second = await pipeline.AnnotatePendingAsync(CancellationToken.None);

        Assert.Equal(0, second);
        Assert.Single(analyzer.AnalyzedStartFens);
    }

    [Fact]
    public async Task Games_still_in_progress_are_not_touched()
    {
        var name = Guid.NewGuid().ToString();
        var unfinished = Guid.NewGuid();
        await QualitySeed.AddGameAsync(name, unfinished, Start, ThreeMoves, finishedAt: null);
        var analyzer = new StubMoveQualityAnalyzer();

        var annotated = await Pipeline(name, analyzer).AnnotatePendingAsync(CancellationToken.None);

        Assert.Equal(0, annotated);
        Assert.Empty(analyzer.AnalyzedStartFens);
        Assert.All((await QualitySeed.LoadMovesAsync(name)).Values, m => Assert.Null(m.CentipawnLoss));
    }

    [Fact]
    public async Task No_op_analyzer_leaves_every_row_untouched()
    {
        var name = Guid.NewGuid().ToString();
        await QualitySeed.AddGameAsync(name, Guid.NewGuid(), Start, ThreeMoves, QualitySeed.T0.AddMinutes(5));

        var annotated = await Pipeline(name, new NoOpMoveQualityAnalyzer()).AnnotatePendingAsync(CancellationToken.None);

        Assert.Equal(0, annotated);
        var moves = await QualitySeed.LoadMovesAsync(name);
        Assert.Equal(3, moves.Count);
        Assert.All(moves.Values, m => Assert.Null(m.CentipawnLoss));
        Assert.All(moves.Values, m => Assert.Null(m.BestSan));
    }

    [Fact]
    public async Task Unavailable_analyzer_is_never_called_even_with_pending_games()
    {
        var name = Guid.NewGuid().ToString();
        await QualitySeed.AddGameAsync(name, Guid.NewGuid(), Start, ThreeMoves, QualitySeed.T0.AddMinutes(5));
        var analyzer = new StubMoveQualityAnalyzer { IsAvailable = false };

        Assert.Equal(0, await Pipeline(name, analyzer).AnnotatePendingAsync(CancellationToken.None));
        Assert.Empty(analyzer.AnalyzedStartFens);
    }

    [Fact]
    public async Task Games_are_scored_one_at_a_time_in_finish_order()
    {
        var name = Guid.NewGuid().ToString();
        var late = "late";
        var early = "early";
        var middle = "middle";
        await QualitySeed.AddGameAsync(name, Guid.NewGuid(), late, ThreeMoves, QualitySeed.T0.AddMinutes(30));
        await QualitySeed.AddGameAsync(name, Guid.NewGuid(), early, ThreeMoves, QualitySeed.T0.AddMinutes(10));
        await QualitySeed.AddGameAsync(name, Guid.NewGuid(), middle, ThreeMoves, QualitySeed.T0.AddMinutes(20));
        var analyzer = new StubMoveQualityAnalyzer();

        var annotated = await Pipeline(name, analyzer).AnnotatePendingAsync(CancellationToken.None);

        Assert.Equal(3, annotated);
        Assert.Equal([early, middle, late], analyzer.AnalyzedStartFens);
        Assert.Equal(1, analyzer.MaxInFlight);
    }

    [Fact]
    public async Task A_game_that_fails_is_left_untouched_and_does_not_block_the_games_behind_it()
    {
        var name = Guid.NewGuid().ToString();
        var failing = Guid.NewGuid();
        var healthy = Guid.NewGuid();
        await QualitySeed.AddGameAsync(name, failing, "broken", ThreeMoves, QualitySeed.T0.AddMinutes(1));
        await QualitySeed.AddGameAsync(name, healthy, Start, ThreeMoves, QualitySeed.T0.AddMinutes(2));
        var analyzer = new StubMoveQualityAnalyzer
        {
            Answer = (startFen, sans) => startFen == "broken"
                ? throw new InvalidOperationException("engine crashed")
                : StubMoveQualityAnalyzer.DefaultAnswer(startFen, sans),
        };

        var annotated = await Pipeline(name, analyzer).AnnotatePendingAsync(CancellationToken.None);

        Assert.Equal(1, annotated);
        var moves = await QualitySeed.LoadMovesAsync(name);
        Assert.All(moves.Where(kv => kv.Key.GameId == failing).Select(kv => kv.Value), m => Assert.Null(m.CentipawnLoss));
        Assert.All(moves.Where(kv => kv.Key.GameId == healthy).Select(kv => kv.Value), m => Assert.NotNull(m.CentipawnLoss));
    }

    [Fact]
    public async Task An_analyzer_answer_that_does_not_cover_every_move_writes_nothing_for_that_game()
    {
        var name = Guid.NewGuid().ToString();
        var game = Guid.NewGuid();
        await QualitySeed.AddGameAsync(name, game, Start, ThreeMoves, QualitySeed.T0.AddMinutes(5));
        var analyzer = new StubMoveQualityAnalyzer
        {
            Answer = (_, sans) => [new MoveQuality(1, 10, "best1")],
        };

        var annotated = await Pipeline(name, analyzer).AnnotatePendingAsync(CancellationToken.None);

        Assert.Equal(0, annotated);
        var moves = await QualitySeed.LoadMovesAsync(name);
        Assert.All(moves.Values, m => Assert.Null(m.CentipawnLoss));
    }

    [Fact]
    public async Task Cancellation_is_not_swallowed_as_a_failed_game()
    {
        var name = Guid.NewGuid().ToString();
        var game = Guid.NewGuid();
        await QualitySeed.AddGameAsync(name, game, Start, ThreeMoves, QualitySeed.T0.AddMinutes(5));
        using var cts = new CancellationTokenSource();
        var analyzer = new StubMoveQualityAnalyzer { BeforeAnswer = cts.Cancel };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Pipeline(name, analyzer).AnnotatePendingAsync(cts.Token));

        var moves = await QualitySeed.LoadMovesAsync(name);
        Assert.All(moves.Values, m => Assert.Null(m.CentipawnLoss));
    }
}
