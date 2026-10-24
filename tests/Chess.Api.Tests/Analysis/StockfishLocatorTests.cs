using Chess.Analysis;
using Xunit;

namespace Chess.Api.Tests.Analysis;

public class StockfishLocatorTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("stockfish-locator").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Configured_path_that_exists_is_used()
    {
        var binary = Path.Combine(_root, "my-engine");
        File.WriteAllText(binary, "");

        Assert.Equal(binary, StockfishLocator.Resolve(binary, pathVariable: null));
    }

    [Fact]
    public void Configured_path_that_is_missing_is_not_replaced_by_one_on_PATH()
    {
        var onPath = Path.Combine(_root, "bin");
        Directory.CreateDirectory(onPath);
        File.WriteAllText(Path.Combine(onPath, "stockfish"), "");

        Assert.Null(StockfishLocator.Resolve(Path.Combine(_root, "missing"), onPath));
    }

    [Fact]
    public void Without_configuration_the_first_stockfish_on_PATH_is_used()
    {
        var first = Path.Combine(_root, "first");
        var second = Path.Combine(_root, "second");
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(second);
        var fileName = OperatingSystem.IsWindows() ? "stockfish.exe" : "stockfish";
        File.WriteAllText(Path.Combine(second, fileName), "");
        File.WriteAllText(Path.Combine(first, fileName), "");
        var pathVariable = string.Join(Path.PathSeparator, first, second);

        Assert.Equal(Path.Combine(first, fileName), StockfishLocator.Resolve(null, pathVariable));
    }

    [Fact]
    public void Without_configuration_and_without_stockfish_on_PATH_there_is_no_binary()
    {
        Assert.Null(StockfishLocator.Resolve("", _root));
        Assert.Null(StockfishLocator.Resolve(null, null));
    }
}
