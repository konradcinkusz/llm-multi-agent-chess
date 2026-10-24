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
