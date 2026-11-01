using Chess.Engine;

namespace Chess.Api.Results.Archive;

/// <summary>
/// Checks an archived game by replaying it on the engine: every move must be legal, the game must not go on after it ended, and
/// the result must be one the rules (or a forfeit, or an adjudication) can produce. A replay proves the game is a possible game; it
/// cannot prove which model played it, so the archive stays a matter of review (docs/RESULTS-ARCHIVE.md).
/// </summary>
public static class GameRecordValidator
{
    public const int MaxMoves = 1000;
    public const int MaxModelLength = 100;

    private static readonly string[] Results = ["1-0", "0-1", "1/2-1/2"];
    private static readonly string[] Kinds = ["llm", "random"];

    // Terminations the rules decide (the board must agree) and those the runner decides (the board must still be in play).
    private static readonly string[] BoardTerminations = ["checkmate", "stalemate", "insufficient-material", "fifty-move", "threefold"];
    private static readonly string[] RunnerTerminations = ["forfeit-white", "forfeit-black", "adjudicated", "budget"];

    public static IReadOnlyList<string> Validate(GameRecord record)
    {
        var errors = new List<string>();
        if (record.Version != GameRecord.CurrentVersion)
        {
            errors.Add($"version must be {GameRecord.CurrentVersion}, not {record.Version}");
        }

        if (record.Id == Guid.Empty) errors.Add("id must not be empty");
        CheckSide("white", record.White, errors);
        CheckSide("black", record.Black, errors);
        if (!Results.Contains(record.Result)) errors.Add($"result '{record.Result}' must be one of {string.Join(", ", Results)}");
        if (!BoardTerminations.Contains(record.Termination) && !RunnerTerminations.Contains(record.Termination))
        {
            errors.Add($"termination '{record.Termination}' is not a known termination");
        }

        if (record.Moves is null || record.Moves.Count > MaxMoves)
        {
            errors.Add($"moves must hold at most {MaxMoves} plies");
            return errors;
        }

        if (record.PlayedAt.Year < 2024) errors.Add("playedAt must be a date from 2024 on");
        if (errors.Count > 0) return errors;

        var board = BoardOf(record.StartFen, errors);
        if (board is null) return errors;

        Replay(record, board, errors);
        return errors;
    }

    private static void CheckSide(string name, RecordSide? side, List<string> errors)
    {
        if (side is null || !Kinds.Contains(side.Kind))
        {
            errors.Add($"{name}.kind must be llm or random");
            return;
        }

        if (string.IsNullOrWhiteSpace(side.Model) || side.Model.Length > MaxModelLength || side.Model.Any(char.IsControl))
        {
            errors.Add($"{name}.model must be 1 to {MaxModelLength} characters without control characters");
        }
        else if (side.Kind == "random" && side.Model != "random")
        {
            errors.Add($"{name}.model must be 'random' for a random side");
        }
    }

    private static ChessBoard? BoardOf(string? startFen, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(startFen)) return ChessBoard.StartingPosition();
        try
        {
            var board = new ChessBoard();
            board.LoadFen(startFen);
            return board;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            errors.Add($"startFen '{startFen}' is not a position: {ex.Message}");
            return null;
        }
    }

    private static void Replay(GameRecord record, ChessBoard board, List<string> errors)
    {
        for (var i = 0; i < record.Moves.Count; i++)
        {
            var move = record.Moves[i];
            var ply = i + 1;
            if (Games.GameTermination.FromStatus(board) is not null)
            {
                errors.Add($"ply {ply}: the game was already over");
                return;
            }

            if (string.IsNullOrEmpty(move.San) || !board.ApplyMoveSan(move.San))
            {
                errors.Add($"ply {ply}: '{move.San}' is not a legal move in {board.ToFen()}");
                return;
            }

            CheckMoveFigures(ply, move, errors);
        }

        CheckEnd(record, board, errors);
    }

    private static void CheckMoveFigures(int ply, RecordMove move, List<string> errors)
    {
        if (move.Attempts is < 1 or > 20) errors.Add($"ply {ply}: attempts must be 1 to 20");
        if (move.LatencyMs is < 0 or > 3_600_000) errors.Add($"ply {ply}: latencyMs must be 0 to 3600000");
        if (move.InputTokens is < 0 || move.OutputTokens is < 0) errors.Add($"ply {ply}: tokens must not be negative");
        if (move.CentipawnLoss is < 0 or > 100_000) errors.Add($"ply {ply}: centipawnLoss must be 0 to 100000");
    }

    private static void CheckEnd(GameRecord record, ChessBoard board, List<string> errors)
    {
        var ending = Games.GameTermination.FromStatus(board);
        if (BoardTerminations.Contains(record.Termination))
        {
            if (ending is null || ending.Termination != record.Termination || ending.Result != record.Result)
            {
                errors.Add($"the final position does not end the game as {record.Termination} {record.Result}");
            }

            return;
        }

        if (ending is not null)
        {
            errors.Add($"the final position is {ending.Termination}, but the record says {record.Termination}");
            return;
        }

        var forfeitedSide = record.Termination switch
        {
            "forfeit-white" => Side.White,
            "forfeit-black" => Side.Black,
            _ => (Side?)null,
        };
        if (forfeitedSide is { } side && (board.SideToMove != side || record.Result != (side == Side.White ? "0-1" : "1-0")))
        {
            errors.Add($"{record.Termination} needs {side} to move and the other side to win");
        }
    }
}
