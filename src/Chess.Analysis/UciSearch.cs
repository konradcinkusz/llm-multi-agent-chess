namespace Chess.Analysis;

/// <summary>Result of one fixed-depth search: score from the side to move (mate = ±1000), best move in UCI or null.</summary>
internal sealed record UciSearch(int Score, string? BestMove);

/// <summary>Reads the lines a UCI engine prints for one <c>go</c> command.</summary>
internal static class UciTranscript
{
    private const string BestMovePrefix = "bestmove ";

    public static UciSearch Parse(IEnumerable<string> lines)
    {
        int? score = null;
        string? bestMove = null;
        var sawBestMove = false;

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.StartsWith("info ", StringComparison.Ordinal) && !line.StartsWith("info string", StringComparison.Ordinal))
            {
                // Engines print progressively deeper lines; the last one with a score is the final result.
                if (TryParseScore(line, out var found))
                {
                    score = found;
                }
            }
            else if (line.StartsWith(BestMovePrefix, StringComparison.Ordinal))
            {
                sawBestMove = true;
                var move = line[BestMovePrefix.Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
                bestMove = move == "(none)" ? null : move;
            }
        }

        if (!sawBestMove)
        {
            throw new FormatException("UCI transcript has no bestmove line.");
        }

        return score is { } value
            ? new UciSearch(value, bestMove)
            : throw new FormatException("UCI transcript has no score line.");
    }

    private static bool TryParseScore(string info, out int score)
    {
        var tokens = info.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var at = Array.IndexOf(tokens, "score");
        if (at < 0 || at + 2 >= tokens.Length || !int.TryParse(tokens[at + 2], out var value))
        {
            score = 0;
            return false;
        }

        score = tokens[at + 1] switch
        {
            "cp" => value,
            "mate" => value > 0 ? CentipawnMath.MateScore : -CentipawnMath.MateScore,
            _ => 0
        };
        return tokens[at + 1] is "cp" or "mate";
    }
}
