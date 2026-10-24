using Chess.Analysis;
using Xunit;

namespace Chess.Api.Tests.Analysis;

/// <summary>Runs against a real Stockfish. Skipped unless <c>STOCKFISH_PATH</c> is set.</summary>
public class StockfishIntegrationTests
{
    private const string StartFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    private static string Binary => Environment.GetEnvironmentVariable("STOCKFISH_PATH")!;

    [StockfishFact]
    public async Task Real_stockfish_scores_the_move_that_allows_mate_and_the_mate_itself()
    {
        // 1.f3 e5 2.g4?? Qh4# — the second move gives mate; Qh4# is the best move there.
        await using var analyzer = new StockfishAnalyzer(Binary, depth: 12);

        var quality = await analyzer.AnalyzeAsync(StartFen, ["f3", "e5", "g4", "Qh4#"], CancellationToken.None);

        Assert.Equal(4, quality.Count);
        Assert.True(quality[2].CentipawnLoss >= 500, $"g4 should lose at least 500 cp, got {quality[2].CentipawnLoss}.");
        Assert.Equal(0, quality[3].CentipawnLoss);
        Assert.Equal("Qh4#", quality[3].BestSan);
    }

    [StockfishFact]
    public async Task Real_stockfish_evaluates_the_start_position_near_equal()
    {
        await using var analyzer = new StockfishAnalyzer(Binary, depth: 12);

        var score = await analyzer.EvaluateAsync(StartFen, CancellationToken.None);

        Assert.NotNull(score);
        Assert.InRange(score!.Value, -150, 150);
    }

    [StockfishFact]
    public async Task A_cancelled_search_retires_the_process_and_the_next_call_starts_a_new_one()
    {
        await using var engine = new ProcessUciEngine(Binary);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        // Depth 60 from the start position runs far longer than the cancellation delay.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => engine.SearchAsync(StartFen, [], depth: 60, cancel.Token));

        var search = await engine.SearchAsync(StartFen, [], depth: 1, CancellationToken.None);

        Assert.NotNull(search.BestMove);
    }
}
