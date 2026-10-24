using Chess.Analysis;
using Chess.Api.Persistence;
using Chess.Api.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Chess.Api.Quality;

/// <summary>
/// Scores the moves of finished games (RATING.md §4): every move gets <c>centipawn_loss</c> and <c>best_san</c>.
/// Games are taken one at a time, oldest finish first. A game that fails is logged and left as it was, so the
/// next pass retries it without holding up the games behind it. Nothing is read or written when the analyzer is
/// unavailable (P8).
/// </summary>
public sealed class MoveQualityPipeline(
    IDbContextFactory<ArenaDbContext> factory,
    IMoveQualityAnalyzer analyzer,
    ILogger<MoveQualityPipeline> logger)
{
    /// <summary>Annotates every finished game that still has an unscored move. Returns how many games were written.</summary>
    public async Task<int> AnnotatePendingAsync(CancellationToken ct)
    {
        if (!analyzer.IsAvailable)
        {
            return 0;
        }

        var annotated = 0;
        foreach (var gameId in await PendingGameIdsAsync(ct))
        {
            try
            {
                if (await AnnotateGameAsync(gameId, ct))
                {
                    annotated++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Move-quality analysis of game {GameId} failed; retrying on a later pass.", gameId);
            }
        }

        return annotated;
    }

    private async Task<List<Guid>> PendingGameIdsAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Games
            .Where(g => g.FinishedAt != null && db.Moves.Any(m => m.GameId == g.Id && m.CentipawnLoss == null))
            .OrderBy(g => g.FinishedAt)
            .ThenBy(g => g.Id)
            .Select(g => g.Id)
            .ToListAsync(ct);
    }

    /// <summary>Scores all moves of one game in a single save, so a game is either fully annotated or untouched.</summary>
    private async Task<bool> AnnotateGameAsync(Guid gameId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var game = await db.Games.AsNoTracking().SingleAsync(g => g.Id == gameId, ct);
        var moves = await db.Moves.Where(m => m.GameId == gameId).OrderBy(m => m.Ply).ToListAsync(ct);
        if (moves.Count == 0)
        {
            return false;
        }

        var quality = await analyzer.AnalyzeAsync(game.StartFen, moves.Select(m => m.San).ToList(), ct);
        var byPly = quality.ToDictionary(q => q.Ply);
        if (quality.Count != moves.Count || moves.Any(m => !byPly.ContainsKey(m.Ply)))
        {
            throw new InvalidOperationException(
                $"Analyzer returned {quality.Count} result(s) for {moves.Count} move(s) in game {gameId}.");
        }

        foreach (var move in moves)
        {
            var scored = byPly[move.Ply];
            move.CentipawnLoss = scored.CentipawnLoss;
            move.BestSan = scored.BestSan;
        }

        await db.SaveChangesAsync(ct);
        return true;
    }
}
