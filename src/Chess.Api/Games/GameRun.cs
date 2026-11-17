using System.Globalization;
using Chess.Analysis;
using Chess.Api.Broadcast;
using Chess.Api.Persistence.Entities;
using Chess.Api.Players;
using Chess.Engine;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

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
    IGamePacer? pacer = null,
    ILogger? logger = null)
{
    /// <summary>Plays the game from its stored position to its end. The events of each ply follow its save.</summary>
    public async Task<GameEnd> PlayAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await using var white = await players.CreateAsync(new PlayerDescriptor(game.WhiteKind, game.WhiteModel), game.Id, Side.White, ct);
        await using var black = await players.CreateAsync(new PlayerDescriptor(game.BlackKind, game.BlackModel), game.Id, Side.Black, ct);

        game.StartedAt ??= clock.GetUtcNow();
        await db.SaveChangesAsync(ct);

        var history = await LoadSansAsync(ct);
        var board = Replay(history);

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

    /// <summary>
    /// Records the end of the game: result, termination, PGN and finish time. Publishing is left to the caller. A ply that
    /// failed to save leaves its inserts in the context's tracker, and saving again would repeat that failure with the end
    /// attached, so the tracker is cleared and the stored game is loaded afresh: only the end is written, over what was saved.
    /// </summary>
    public async Task FinishAsync(GameEnd end, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var stored = await db.Games.FirstAsync(g => g.Id == game.Id, ct);
        var sans = await LoadSansAsync(ct);
        stored.Result = end.Result;
        stored.Termination = end.Termination;
        stored.FinishedAt = clock.GetUtcNow();
        stored.Pgn = Pgn.Write(Tags(stored), stored.StartFen, sans, end.Result, end.Termination);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// The end of a game that reached its budget (03 §15.2): a position that already ends the game keeps its end; otherwise the
    /// game is adjudicated at the board, as at <c>MaxPlies</c>, with the <c>budget</c> termination. The board is replayed from the
    /// stored moves, so a threefold repetition or fifty-move draw that the last move completed is seen.
    /// </summary>
    public async Task<GameEnd> BudgetEndAsync(CancellationToken ct)
    {
        var board = Replay(await LoadSansAsync(ct));
        if (GameTermination.FromStatus(board) is { } ending)
        {
            return ending;
        }

        return GameTermination.Adjudicate(board, await EvaluateAsync(board, ct), GameTermination.BudgetReached);
    }

    /// <summary>
    /// The board after the stored moves, replayed from the start position so that it knows the positions that came before
    /// (repetition). Falls back to the stored position when the moves do not replay.
    /// </summary>
    private ChessBoard Replay(IReadOnlyList<string> sans)
    {
        var board = new ChessBoard();
        board.LoadFen(game.StartFen);
        if (sans.All(board.ApplyMoveSan))
        {
            return board;
        }

        logger?.LogWarning("Game {GameId}: the stored moves do not replay; the board is taken from the stored position", game.Id);
        board = new ChessBoard();
        board.LoadFen(game.PlyCount == 0 ? game.StartFen : game.CurrentFen);
        return board;
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
                Reasoning = attempt.Reasoning,
                ReasoningTokens = attempt.ReasoningTokens,
                CachedInputTokens = attempt.CachedInputTokens,
                AiCredits = attempt.AiCredits,
                RejectionKind = attempt.RejectionKind,
            });
        }

        if (move.Forfeit && IsGatewayFailure(move))
        {
            // The model was never heard: every attempt ended in a gateway error. That is not the model's forfeit and it is not
            // rated; the attempts are kept for the log, and the game ends as aborted (see TechnicalGameFailureException).
            await db.SaveChangesAsync(ct);
            PublishRecorded(ply, color, mover.Descriptor.ModelId, move.Attempts);
            throw new TechnicalGameFailureException(
                $"{mover.Descriptor.ModelId} gave no reply to ply {ply} in game {game.Id}: {move.Attempts[^1].Error}");
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

    /// <summary>
    /// A forfeit in which every attempt was a gateway error (kind <c>error</c>): the move got no reply at all. A timeout, an
    /// illegal or an unparseable reply is the model's own failure and stays a forfeit (RATING.md §2).
    /// </summary>
    internal static bool IsGatewayFailure(PlayerMove move) =>
        move.Attempts.Count > 0 && move.Attempts.All(a => a.RejectionKind == RejectionClassifier.Error);

    private async Task<GameEnd> AdjudicateAsync(ChessBoard board, CancellationToken ct) =>
        GameTermination.Adjudicate(board, await EvaluateAsync(board, ct));

    /// <summary>
    /// Stockfish's verdict when it is there (RATING.md §2); null otherwise, and when the engine fails, so that the material
    /// difference decides. A broken engine must not turn a game of 200 plies into an aborted one.
    /// </summary>
    private async Task<int?> EvaluateAsync(ChessBoard board, CancellationToken ct)
    {
        if (!analyzer.IsAvailable)
        {
            return null;
        }

        try
        {
            return await analyzer.EvaluateAsync(board.ToFen(), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.LogWarning(ex, "Game {GameId}: the engine failed at adjudication; the material rule decides", game.Id);
            return null;
        }
    }

    /// <summary>03 §10: one event per stored attempt, legal or not, so the live log shows each reply as it is kept.</summary>
    private void PublishRecorded(int ply, string color, string modelId, IEnumerable<MoveAttempt> attempts)
    {
        var now = clock.GetUtcNow();
        foreach (var attempt in attempts)
        {
            events.Publish(new AttemptRecorded(game.Id, now, ply, attempt.Number, color, modelId, attempt.Prompt,
                attempt.RawReply, attempt.Reasoning, attempt.ParsedSan, attempt.Legal, (long)attempt.Latency.TotalMilliseconds,
                attempt.InputTokens, attempt.OutputTokens, attempt.ReasoningTokens, attempt.AiCredits, attempt.RejectionKind));
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

    private static Dictionary<string, string> Tags(GameEntity stored) => new(StringComparer.Ordinal)
    {
        ["Event"] = "LLM Chess Arena",
        ["Date"] = stored.CreatedAt.UtcDateTime.ToString("yyyy.MM.dd", CultureInfo.InvariantCulture),
        ["White"] = stored.WhiteModel,
        ["Black"] = stored.BlackModel,
    };

    private static string ColorName(Side side) => side == Side.White ? "white" : "black";

    private static int? SumOrNull(IEnumerable<int?> values)
    {
        var present = values.OfType<int>().ToList();
        return present.Count == 0 ? null : present.Sum();
    }
}
