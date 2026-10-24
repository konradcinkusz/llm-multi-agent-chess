using Xunit;

namespace Chess.Api.Tests.Analysis;

/// <summary>A test that runs only when <c>STOCKFISH_PATH</c> names a Stockfish binary; otherwise it is skipped.</summary>
public sealed class StockfishFactAttribute : FactAttribute
{
    public StockfishFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("STOCKFISH_PATH")))
        {
            Skip = "Set STOCKFISH_PATH to a Stockfish binary to run this test.";
        }
    }
}
