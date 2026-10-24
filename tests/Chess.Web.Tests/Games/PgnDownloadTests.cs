using Chess.Web.Components.Pages.Games;
using Xunit;

namespace Chess.Web.Tests.Games;

public class PgnDownloadTests
{
    private const string Prefix = "data:application/x-chess-pgn;charset=utf-8,";

    [Fact]
    public void TheLinkCarriesTheExactPgnTextIncludingItsQuotesNewlinesAndEscapeCharacters()
    {
        const string pgn = "[Event \"Arena #1 & 50%\"]\n[Result \"1-0\"]\n\n1. e4 e5 2. Nf3 1-0\n";

        var href = PgnDownload.Href(pgn);

        Assert.StartsWith(Prefix, href);
        Assert.Equal(pgn, Uri.UnescapeDataString(href[Prefix.Length..]));
    }

    [Fact]
    public void TheFileIsNamedAsTheApiNamesIt()
    {
        var id = Guid.Parse("44444444-4444-4444-4444-444444444444");

        Assert.Equal("game-44444444-4444-4444-4444-444444444444.pgn", PgnDownload.FileName(id));
    }
}
