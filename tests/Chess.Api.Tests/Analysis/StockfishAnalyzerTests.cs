using Chess.Analysis;
using Xunit;

namespace Chess.Api.Tests.Analysis;

public class StockfishAnalyzerTests
{
    private const string StartFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    [Fact]
    public async Task Each_ply_is_scored_against_the_best_value_before_it()
    {
        // Ply 1: best before = +30 for White; after e4 Black is at +20, so White's e4 gave up 50 cp. Best: d4.
        // Ply 2: best before = +20 for Black; after e5 White is at +10, so Black's e5 gave up 30 cp. Best: c5.
        var engine = new ScriptedUciEngine()
            .Answer(StartFen, "", new UciSearch(30, "d2d4"))
            .Answer(StartFen, "e2e4", new UciSearch(20, "c7c5"))
            .Answer(StartFen, "e2e4 e7e5", new UciSearch(10, "g1f3"));
        await using var analyzer = new StockfishAnalyzer(engine, depth: 12);

        var quality = await analyzer.AnalyzeAsync(StartFen, ["e4", "e5"], CancellationToken.None);

        Assert.Equal([new MoveQuality(1, 50, "d4"), new MoveQuality(2, 30, "c5")], quality);
        Assert.All(engine.Requests, r => Assert.Equal(12, r.Depth));
    }

    [Fact]
    public async Task A_move_that_allows_mate_is_capped_at_1000_cp()
    {
        // Black is at +50 before f6; after f6 White mates (+1000 for White), so the loss is 1050, capped to 1000.
        var engine = new ScriptedUciEngine()
            .Answer(StartFen, "", new UciSearch(0, "d2d4"))
            .Answer(StartFen, "e2e4", new UciSearch(50, "e7e5"))
            .Answer(StartFen, "e2e4 f7f6", new UciSearch(1000, "d1h5"));
        await using var analyzer = new StockfishAnalyzer(engine, depth: 12);

        var quality = await analyzer.AnalyzeAsync(StartFen, ["e4", "f6"], CancellationToken.None);

        Assert.Equal([new MoveQuality(1, 50, "d4"), new MoveQuality(2, 1000, "e5")], quality);
    }

    [Fact]
    public async Task Mated_positions_are_decided_by_the_rules_and_never_sent_to_the_engine()
    {
        // Fool's mate. After Qh4# White has no legal move, so there is no search for that position.
        // Every answer below is needed; the scripted engine throws on any other position.
        var engine = new ScriptedUciEngine()
            .Answer(StartFen, "", new UciSearch(20, "e2e4"))
            .Answer(StartFen, "f2f3", new UciSearch(-30, "e7e5"))
            .Answer(StartFen, "f2f3 e7e5", new UciSearch(10, "d2d4"))
            .Answer(StartFen, "f2f3 e7e5 g2g4", new UciSearch(1000, "d8h4"));
        await using var analyzer = new StockfishAnalyzer(engine, depth: 12);

        var quality = await analyzer.AnalyzeAsync(StartFen, ["f3", "e5", "g4", "Qh4#"], CancellationToken.None);

        Assert.Equal(
        [
            new MoveQuality(1, 0, "e4"),
            new MoveQuality(2, 0, "e5"),
            new MoveQuality(3, 1000, "d4"),
            new MoveQuality(4, 0, "Qh4#"),
        ], quality);
        Assert.Equal(4, engine.Requests.Count);
    }

    [Fact]
    public async Task A_best_move_that_is_not_legal_is_an_invalid_operation_not_a_null_reference()
    {
        var engine = new ScriptedUciEngine().Answer(StartFen, "", new UciSearch(20, "e2e5"));
        await using var analyzer = new StockfishAnalyzer(engine, depth: 12);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => analyzer.AnalyzeAsync(StartFen, ["e4"], CancellationToken.None));

        Assert.Contains("e2e5", ex.Message);
    }

    [Theory]
    [InlineData("4k3/8/8/8/8/8/R7/4K3 w - - 100 80", "fifty-move rule")]
    [InlineData("8/8/8/4k3/8/8/8/4KB2 w - - 0 1", "insufficient material")]
    [InlineData("8/8/8/4k3/8/8/8/4K3 b - - 0 1", "bare kings")]
    [InlineData("7k/5Q2/6K1/8/8/8/8/8 b - - 0 1", "stalemate")]
    public async Task A_position_the_rules_draw_is_evaluated_as_zero_without_asking_the_engine(string fen, string why)
    {
        var engine = new ScriptedUciEngine();
        await using var analyzer = new StockfishAnalyzer(engine, depth: 12);

        var score = await analyzer.EvaluateAsync(fen, CancellationToken.None);

        Assert.True(score == 0, $"{why} should score 0, got {score}");
        Assert.Empty(engine.Requests);
    }

    [Fact]
    public async Task A_game_that_ends_by_threefold_repetition_scores_the_final_position_without_asking_the_engine()
    {
        // 1.Nf3 Nf6 2.Ng1 Ng8 3.Nf3 Nf6 4.Ng1 Ng8: the start position has occurred three times after the last move.
        var played = new[] { "g1f3", "g8f6", "f3g1", "f6g8", "g1f3", "g8f6", "f3g1", "f6g8" };
        var engine = new ScriptedUciEngine();
        for (var k = 0; k < played.Length; k++)
        {
            engine.Answer(StartFen, string.Join(' ', played.Take(k)), new UciSearch(0, played[k]));
        }
        await using var analyzer = new StockfishAnalyzer(engine, depth: 12);

        var quality = await analyzer.AnalyzeAsync(
            StartFen, ["Nf3", "Nf6", "Ng1", "Ng8", "Nf3", "Nf6", "Ng1", "Ng8"], CancellationToken.None);

        Assert.Equal(8, quality.Count);
        Assert.All(quality, q => Assert.Equal(0, q.CentipawnLoss));
        Assert.Equal(8, engine.Requests.Count);
    }

    [Fact]
    public async Task An_illegal_move_is_rejected()
    {
        var engine = new ScriptedUciEngine().Answer(StartFen, "", new UciSearch(20, "e2e4"));
        await using var analyzer = new StockfishAnalyzer(engine, depth: 12);

        await Assert.ThrowsAsync<ArgumentException>(() => analyzer.AnalyzeAsync(StartFen, ["e5"], CancellationToken.None));
    }

    [Fact]
    public async Task An_empty_game_is_analysed_without_asking_the_engine()
    {
        var engine = new ScriptedUciEngine();
        await using var analyzer = new StockfishAnalyzer(engine, depth: 12);

        var quality = await analyzer.AnalyzeAsync(StartFen, [], CancellationToken.None);

        Assert.Empty(quality);
        Assert.Empty(engine.Requests);
    }

    [Fact]
    public async Task Evaluation_is_reported_from_White_point_of_view()
    {
        const string blackToMove = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1";
        var engine = new ScriptedUciEngine().Answer(blackToMove, "", new UciSearch(25, "c7c5"));
        await using var analyzer = new StockfishAnalyzer(engine, depth: 12);

        var score = await analyzer.EvaluateAsync(blackToMove, CancellationToken.None);

        Assert.Equal(-25, score);
    }

    [Fact]
    public async Task Evaluating_a_checkmated_side_is_minus_1000_without_asking_the_engine()
    {
        const string whiteMated = "rnb1kbnr/pppp1ppp/8/4p3/6Pq/5P2/PPPPP2P/RNBQKBNR w KQkq - 1 3";
        var engine = new ScriptedUciEngine();
        await using var analyzer = new StockfishAnalyzer(engine, depth: 12);

        var score = await analyzer.EvaluateAsync(whiteMated, CancellationToken.None);

        Assert.Equal(-1000, score);
        Assert.Empty(engine.Requests);
    }

    [Fact]
    public void Depth_must_be_at_least_one()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new StockfishAnalyzer(new ScriptedUciEngine(), depth: 0));
    }

    [Fact]
    public async Task Without_a_binary_move_quality_is_unavailable_and_writes_nothing()
    {
        IMoveQualityAnalyzer analyzer = new NoOpMoveQualityAnalyzer();

        Assert.False(analyzer.IsAvailable);
        Assert.Empty(await analyzer.AnalyzeAsync(StartFen, ["e4"], CancellationToken.None));
        Assert.Null(await analyzer.EvaluateAsync(StartFen, CancellationToken.None));
    }
}
