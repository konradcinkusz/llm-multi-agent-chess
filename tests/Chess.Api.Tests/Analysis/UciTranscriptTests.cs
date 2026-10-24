using Chess.Analysis;
using Xunit;

namespace Chess.Api.Tests.Analysis;

public class UciTranscriptTests
{
    [Fact]
    public void Recorded_search_gives_the_deepest_score_and_its_best_move()
    {
        var search = UciTranscript.Parse(FixtureFile.Lines("uci_startpos_depth8.txt"));

        Assert.Equal(new UciSearch(40, "e2e4"), search);
    }

    [Fact]
    public void Recorded_mate_in_one_for_the_side_to_move_is_plus_1000()
    {
        var search = UciTranscript.Parse(FixtureFile.Lines("uci_back_rank_mate_in_one.txt"));

        Assert.Equal(new UciSearch(1000, "a1a8"), search);
    }

    [Fact]
    public void Recorded_checkmate_of_the_side_to_move_is_minus_1000_with_no_best_move()
    {
        var search = UciTranscript.Parse(FixtureFile.Lines("uci_checkmated_side_to_move.txt"));

        Assert.Equal(new UciSearch(-1000, null), search);
    }

    [Fact]
    public void Mate_against_the_side_to_move_is_minus_1000()
    {
        var search = UciTranscript.Parse(["info depth 9 score mate -3 pv a1a8", "bestmove a1a8 ponder h8g8"]);

        Assert.Equal(-1000, search.Score);
        Assert.Equal("a1a8", search.BestMove);
    }

    [Fact]
    public void Centipawn_scores_are_taken_as_they_are_and_negatives_are_kept()
    {
        var search = UciTranscript.Parse(["info depth 12 score cp -137 nodes 9 pv e7e5", "bestmove e7e5"]);

        Assert.Equal(-137, search.Score);
    }

    [Fact]
    public void Info_string_lines_are_never_read_as_scores()
    {
        var search = UciTranscript.Parse(
        [
            "info depth 1 score cp 5 pv e2e4",
            "info string score cp 999 from a diagnostic line",
            "bestmove e2e4",
        ]);

        Assert.Equal(5, search.Score);
    }

    [Fact]
    public void Transcript_without_a_bestmove_is_rejected()
    {
        Assert.Throws<FormatException>(() => UciTranscript.Parse(["info depth 3 score cp 12 pv e2e4"]));
    }

    [Fact]
    public void Transcript_without_a_score_is_rejected()
    {
        Assert.Throws<FormatException>(() => UciTranscript.Parse(["info string nothing to see", "bestmove e2e4"]));
    }
}
