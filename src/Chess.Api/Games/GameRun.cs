using System.Globalization;
using Chess.Analysis;
using Chess.Api.Persistence.Entities;
using Chess.Api.Players;
using Chess.Engine;
using Microsoft.EntityFrameworkCore;

namespace Chess.Api.Games;

/// <summary>
/// One game being played: its plies and the persistence of each one. It owns the DbContext of the game, so each ply
/// is one <c>SaveChanges</c>. Only <see cref="GameRunner"/> creates and uses it.
/// </summary>
internal sealed class GameRun(
    ArenaDbContext db,
    GameEntity game,
    IChessPlayerFactory players,
    IMoveQualityAnalyzer analyzer,
    IGameEventStream events,
    GameRunnerOptions limits,
    TimeProvider clock,
    IGamePacer? pacer = null)
{
    /// <summary>Plays the game from its stored position to its end. The events of each ply follow its save.</summary>
    public async Task<GameEnd> PlayAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await using var white = await players.CreateAsync(new PlayerDescriptor(game.WhiteKind, game.WhiteModel), game.Id, Side.White, ct);
        await using var black = await players.CreateAsync(new PlayerDescriptor(game.BlackKind, game.BlackModel), game.Id, Side.Black, ct);

        game.StartedAt ??= clock.GetUtcNow();
        await db.SaveChangesAsync(ct);

        var board = new ChessBoard();
        board.LoadFen(game.PlyCount == 0 ? game.StartFen : game.CurrentFen);
        var history = await LoadSansAsync(ct);

        while (true)
        {
            if (GameTermination.FromStatus(board) is { } ending)
            {
                return ending;
            }

            if (game.PlyCount >= limits.MaxPlies)
            {
                return await AdjudicateAsync(board, ct);
            }

            var mover = board.SideToMove == Side.White ? white : black;
            if (await PlayPlyAsync(board, history, mover, ct) is { } forfeit)
            {
                return forfeit;
            }

            if (pacer is not null)
            {
                await pacer.AfterMoveAsync(ct);
            }
        }
    }

    /// <summary>Records the end of the game: result, termination, PGN and finish time. Publishing is left to the caller.</summary>
    public async Task FinishAsync(GameEnd end, CancellationToken ct)
    {
        var sans = await LoadSansAsync(ct);
        game.Result = end.Result;
        game.Termination = end.Termination;
        game.FinishedAt = clock.GetUtcNow();
        game.Pgn = Pgn.Write(Tags(), game.StartFen, sans, end.Result);
        await db.SaveChangesAsync(ct);
    }

    /// <returns>The forfeit when the mover gave up the move; null when a legal move was played.</returns>
    private async Task<GameEnd?> PlayPlyAsync(ChessBoard board, List<string> history, IChessPlayer mover, CancellationToken ct)
    {
        var side = board.SideToMove;
        var ply = game.PlyCount + 1;
        var color = ColorName(side);
        events.Publish(new MoveStarted(game.Id, clock.GetUtcNow(), ply, color, mover.Descriptor.ModelId));

        var legal = board.GetLegalMoves().Select(m => m.San).ToList();
        var move = await mover.ChooseMoveAsync(new MoveRequest(game.Id, ply, board.ToFen(), legal, history), ct);

        var now = clock.GetUtcNow();
        foreach (var attempt in move.Attempts)
        {
            db.MoveAttempts.Add(new MoveAttemptEntity
            {
                GameId = game.Id,
                Ply = ply,
                Number = attempt.Number,
                Prompt = attempt.Prompt,
                RawReply = attempt.RawReply,
                ParsedSan = attempt.ParsedSan,
                Legal = attempt.Legal,
                LatencyMs = (long)attempt.Latency.TotalMilliseconds,
                Error = attempt.Error,
                CreatedAt = now,
                InputTokens = attempt.InputTokens,
                OutputTokens = attempt.OutputTokens,
            });
        }

        if (move.Forfeit)
        {
            await db.SaveChangesAsync(ct);
            PublishRecorded(ply, color, mover.Descriptor.ModelId, move.Attempts);
            PublishRejected(ply, move.Attempts);
            return GameTermination.Forfeit(side);
        }

        var san = move.San ?? throw new InvalidOperationException("A move that is not a forfeit has no SAN.");
        if (!board.ApplyMoveSan(san))
        {
            throw new InvalidOperationException($"Player offered '{san}', which is not legal in '{board.ToFen()}'.");
        }

        history.Add(san);
        game.PlyCount = ply;
        game.CurrentFen = board.ToFen();
        var latencyMs = move.Attempts.Sum(a => (long)a.Latency.TotalMilliseconds);
        db.Moves.Add(new MoveEntity
        {
            GameId = game.Id,
            Ply = ply,
            Color = color,
            San = san,
            FenAfter = game.CurrentFen,
            Attempts = move.Attempts.Count,
            LatencyMs = latencyMs,
            InputTokens = SumOrNull(move.Attempts.Select(a => a.InputTokens)),
            OutputTokens = SumOrNull(move.Attempts.Select(a => a.OutputTokens)),
            PlayedAt = now,
        });
        await db.SaveChangesAsync(ct);

        PublishRecorded(ply, color, mover.Descriptor.ModelId, move.Attempts);
        PublishRejected(ply, move.Attempts);
        events.Publish(new MovePlayed(game.Id, clock.GetUtcNow(), ply, color, san, game.CurrentFen, move.Attempts.Count, latencyMs));
        return null;
    }

    private async Task<GameEnd> AdjudicateAsync(ChessBoard board, CancellationToken ct)
    {
        // Stockfish's verdict when it is there (RATING.md §2); the material difference otherwise.
        int? whiteCentipawns = analyzer.IsAvailable ? await analyzer.EvaluateAsync(board.ToFen(), ct) : null;
        return GameTermination.Adjudicate(board, whiteCentipawns);
    }

    /// <summary>03 §10: one event per stored attempt, legal or not, so the live log shows each reply as it is kept.</summary>
    private void PublishRecorded(int ply, string color, string modelId, IEnumerable<MoveAttempt> attempts)
    {
        var now = clock.GetUtcNow();
        foreach (var attempt in attempts)
        {
            events.Publish(new AttemptRecorded(game.Id, now, ply, attempt.Number, color, modelId, attempt.Prompt,
                attempt.RawReply, Reasoning: null, attempt.ParsedSan, attempt.Legal, (long)attempt.Latency.TotalMilliseconds,
                attempt.InputTokens, attempt.OutputTokens, ReasoningTokens: null, AiCredits: null, RejectionKind: null));
        }
    }

    private void PublishRejected(int ply, IEnumerable<MoveAttempt> attempts)
    {
        var now = clock.GetUtcNow();
        foreach (var attempt in attempts.Where(a => !a.Legal))
        {
            events.Publish(new AttemptRejected(game.Id, now, ply, attempt.Number, attempt.RawReply, attempt.Error ?? "Not a legal move."));
        }
    }

    private Task<List<string>> LoadSansAsync(CancellationToken ct) =>
        db.Moves.Where(m => m.GameId == game.Id).OrderBy(m => m.Ply).Select(m => m.San).ToListAsync(ct);

    private Dictionary<string, string> Tags() => new(StringComparer.Ordinal)
    {
        ["Event"] = "LLM Chess Arena",
        ["Date"] = game.CreatedAt.UtcDateTime.ToString("yyyy.MM.dd", CultureInfo.InvariantCulture),
        ["White"] = game.WhiteModel,
        ["Black"] = game.BlackModel,
    };

    private static string ColorName(Side side) => side == Side.White ? "white" : "black";

    private static int? SumOrNull(IEnumerable<int?> values)
    {
        var present = values.OfType<int>().ToList();
        return present.Count == 0 ? null : present.Sum();
    }
}
