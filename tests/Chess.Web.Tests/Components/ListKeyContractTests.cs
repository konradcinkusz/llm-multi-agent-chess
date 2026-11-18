using Xunit;

namespace Chess.Web.Tests.Components;

/// <summary>
/// The lists that show newest rows first are keyed. Without <c>@key</c> Blazor reuses the element at the same position, so an
/// open details panel (an engine entry or an attempt) kept showing the row at its position when a newer one arrived: the
/// panel opened on ply 4 later showed ply 6. bUnit parses the markup afresh on every query, so element identity cannot be
/// observed there; the markup that carries the key is pinned instead.
/// </summary>
public class ListKeyContractTests
{
    [Theory]
    [InlineData("Shared/Game/EngineLog.razor", "<li @key=\"(entry.Ply, entry.Attempt)\"")]
    [InlineData("Pages/Admin/AttemptsPage.razor", "<li @key=\"(attempt.GameId, attempt.Ply, attempt.Number)\"")]
    [InlineData("Shared/Game/MoveTable.razor", "<li @key=\"row.Number\"")]
    [InlineData("Pages/Games/GamesPage.razor", "<tr @key=\"game.Id\"")]
    public void TheRowsOfANewestFirstListAreKeyed(string file, string expected)
    {
        var path = Path.Combine(FindRepoRoot(), "src", "Chess.Web", "Components", file);

        Assert.Contains(expected, File.ReadAllText(path), StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Chess.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Chess.sln was not found above the test binaries.");
    }
}
