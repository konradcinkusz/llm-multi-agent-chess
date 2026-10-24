using Chess.Engine;
using Xunit;

namespace Chess.Engine.Tests;

public class PgnTests
{
    private const string Start = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
    private const string AfterE4 = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1";

    private static readonly IReadOnlyDictionary<string, string> NoTags = new Dictionary<string, string>();

    [Fact]
    public void ScholarsMateMatchesFixtureByteForByte()
    {
        var tags = new Dictionary<string, string>
        {
            ["Event"] = "Casual", ["Site"] = "Internet", ["Date"] = "2026.10.24",
            ["Round"] = "1", ["White"] = "Alice", ["Black"] = "Bob",
        };
        var moves = new[] { "e4", "e5", "Bc4", "Nc6", "Qh5", "Nf6", "Qxf7#" };

        var pgn = Pgn.Write(tags, null, moves, "1-0");

        var expected = string.Join('\n',
            "[Event \"Casual\"]",
            "[Site \"Internet\"]",
            "[Date \"2026.10.24\"]",
            "[Round \"1\"]",
            "[White \"Alice\"]",
            "[Black \"Bob\"]",
            "[Result \"1-0\"]",
            "",
            "1. e4 e5 2. Bc4 Nc6 3. Qh5 Nf6 4. Qxf7# 1-0") + "\n";
        Assert.Equal(expected, pgn);
    }

    [Fact]
    public void MissingRosterTagsFallBackToPlaceholders()
    {
        var pgn = Pgn.Write(NoTags, null, new[] { "e4" }, "*");

        var expected = string.Join('\n',
            "[Event \"?\"]",
            "[Site \"?\"]",
            "[Date \"????.??.??\"]",
            "[Round \"?\"]",
            "[White \"?\"]",
            "[Black \"?\"]",
            "[Result \"*\"]",
            "",
            "1. e4 *") + "\n";
        Assert.Equal(expected, pgn);
    }

    [Fact]
    public void StandardStartWritesNoSetUpOrFenTags()
    {
        var pgn = Pgn.Write(NoTags, Start, new[] { "e4" }, "*");

        Assert.DoesNotContain("SetUp", pgn);
        Assert.DoesNotContain("FEN", pgn);
    }

    [Fact]
    public void NonStandardStartWritesSetUpFenAndBlackMoveNumbers()
    {
        var pgn = Pgn.Write(NoTags, AfterE4, new[] { "e5", "Nf3" }, "*");

        var expected = string.Join('\n',
            "[Event \"?\"]",
            "[Site \"?\"]",
            "[Date \"????.??.??\"]",
            "[Round \"?\"]",
            "[White \"?\"]",
            "[Black \"?\"]",
            "[Result \"*\"]",
            "[SetUp \"1\"]",
            $"[FEN \"{AfterE4}\"]",
            "",
            "1... e5 2. Nf3 *") + "\n";
        Assert.Equal(expected, pgn);
    }

    [Fact]
    public void ResultArgumentOverridesResultTagAndExtraTagsFollowRosterInOrder()
    {
        var tags = new Dictionary<string, string>
        {
            ["Result"] = "0-1", ["WhiteElo"] = "1500", ["Annotator"] = "X",
        };

        var pgn = Pgn.Write(tags, null, new[] { "e4" }, "1-0");

        var header = pgn.Split('\n').TakeWhile(line => line.StartsWith('[')).ToArray();
        Assert.Equal(
            new[]
            {
                "[Event \"?\"]", "[Site \"?\"]", "[Date \"????.??.??\"]", "[Round \"?\"]",
                "[White \"?\"]", "[Black \"?\"]", "[Result \"1-0\"]",
                "[Annotator \"X\"]", "[WhiteElo \"1500\"]",
            },
            header);
    }

    [Fact]
    public void TagValuesEscapeQuotesAndBackslashes()
    {
        var tags = new Dictionary<string, string> { ["Event"] = "Say \"hi\" \\ bye" };

        var pgn = Pgn.Write(tags, null, Array.Empty<string>(), "*");

        Assert.StartsWith("[Event \"Say \\\"hi\\\" \\\\ bye\"]\n", pgn);
    }

    [Fact]
    public void MovetextWrapsAtEightyColumnsWithoutSplittingMoves()
    {
        var shuffle = new[] { "Nf3", "Nf6", "Ng1", "Ng8" };
        var moves = Enumerable.Range(0, 10).SelectMany(_ => shuffle).ToList();

        var pgn = Pgn.Write(NoTags, null, moves, "*");

        var lines = Movetext(pgn);
        Assert.True(lines.Length > 1, "movetext should span several lines");
        Assert.All(lines, line => Assert.True(line.Length <= 80, line));
        var expected = string.Join(' ', moves.Select((san, i) => i % 2 == 0 ? $"{i / 2 + 1}. {san}" : san)) + " *";
        Assert.Equal(expected, string.Join(' ', lines));
    }

    [Fact]
    public void IllegalMoveIsRejected()
    {
        Assert.Throws<ArgumentException>(() => Pgn.Write(NoTags, null, new[] { "Nd4" }, "*"));
    }

    [Theory]
    [InlineData("1-1")]
    [InlineData("")]
    [InlineData("win")]
    public void UnknownResultIsRejected(string result)
    {
        Assert.Throws<ArgumentException>(() => Pgn.Write(NoTags, null, Array.Empty<string>(), result));
    }

    [Fact]
    public void TagNameWithSpaceIsRejected()
    {
        var tags = new Dictionary<string, string> { ["Bad Name"] = "x" };

        Assert.Throws<ArgumentException>(() => Pgn.Write(tags, null, Array.Empty<string>(), "*"));
    }

    private static string[] Movetext(string pgn)
        => pgn.Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(line => !line.StartsWith('[')).ToArray();
}
