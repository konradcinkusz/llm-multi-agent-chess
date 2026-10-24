using Chess.Api.Persistence;
using Chess.Api.Persistence.Entities;
using Chess.Rating;
using Microsoft.EntityFrameworkCore;

namespace Chess.Api.Results;

/// <summary>
/// Appends <c>rating_snapshots</c> for finished rated games (03 §4, ADR-007). One batch is the rating report
/// over every rated game up to and including a game, taken in finish order, with one row per model. The newest
/// snapshot row names the last game it covers, so a restart resumes after it and never writes a batch twice.
/// Snapshots are history only: the only thing read back is that cursor.
/// </summary>
public static class RatingSnapshotWriter
{
    public const string FormulaVersion = "1";

    /// <summary>Writes one batch for every rated game finished after the newest snapshot.</summary>
    /// <returns>The number of batches written.</returns>
    public static async Task<int> WriteNewAsync(ArenaDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var cursor = await db.RatingSnapshots
            .OrderByDescending(s => s.Id)
            .Select(s => (Guid?)s.AfterGameId)
            .FirstOrDefaultAsync(ct);

        var games = await ResultsGames.RatedDecided(db)
            .OrderBy(g => g.FinishedAt).ThenBy(g => g.Id)
            .Select(ResultsGames.Project)
            .ToListAsync(ct);

        var cursorIndex = cursor is null ? -1 : games.FindIndex(g => g.Id == cursor.Value);
        if (cursor is not null && cursorIndex < 0)
        {
            throw new InvalidOperationException(
                $"rating_snapshots points at game {cursor}, which is not a finished rated game.");
        }

        var firstNew = cursorIndex + 1;
        if (firstNew >= games.Count)
        {
            return 0;
        }

        var movesByGame = (await ResultsGames.MovesOf(db, ResultsGames.RatedDecided(db)).ToListAsync(ct))
            .GroupBy(m => m.GameId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var outcomes = new List<GameOutcome>(games.Count);
        var tally = new MoveTally();
        var batches = 0;
        for (var i = 0; i < games.Count; i++)
        {
            var game = games[i];
            outcomes.Add(new GameOutcome(game.WhiteModel, game.BlackModel, ResultsGames.WhiteScore(game.Result)));
            if (movesByGame.TryGetValue(game.Id, out var moves))
            {
                foreach (var move in moves)
                {
                    tally.Add(game, move);
                }
            }

            if (i < firstNew)
            {
                continue;
            }

            var report = RatingCalculator.Compute(outcomes, tally.Stats(), new RatingOptions());
            var takenAt = clock.GetUtcNow();
            foreach (var rating in report.Models)
            {
                db.RatingSnapshots.Add(new RatingSnapshotEntity
                {
                    TakenAt = takenAt,
                    AfterGameId = game.Id,
                    RatedGames = report.Games,
                    Model = rating.Model,
                    Rating = rating.Rating,
                    CiLow = rating.CiLow,
                    CiHigh = rating.CiHigh,
                    ArenaScore = rating.ArenaScore,
                    Games = rating.Games,
                    Provisional = rating.Provisional,
                    FormulaVersion = FormulaVersion,
                });
            }

            await db.SaveChangesAsync(ct);
            batches++;
        }

        return batches;
    }

    /// <summary>Move statistics of LLM sides over the games seen so far (RATING.md §4).</summary>
    private sealed class MoveTally
    {
        private readonly Dictionary<string, Counts> _byModel = new(StringComparer.Ordinal);

        public void Add(ScoredGame game, MoveRow move)
        {
            var (kind, model) = ResultsGames.Mover(game, move.FenAfter);
            if (kind != ResultsGames.LlmKind)
            {
                return;
            }

            if (!_byModel.TryGetValue(model, out var counts))
            {
                _byModel[model] = counts = new Counts();
            }

            counts.Moves++;
            if (move.Attempts == 1)
            {
                counts.FirstAttemptLegal++;
            }

            if (move.CentipawnLoss is { } loss)
            {
                counts.CentipawnLossSum += loss;
                counts.CentipawnLossCount++;
            }
        }

        public IReadOnlyList<PlayerMoveStats> Stats() => _byModel
            .Select(entry => new PlayerMoveStats(
                entry.Key,
                entry.Value.Moves,
                entry.Value.FirstAttemptLegal,
                entry.Value.CentipawnLossCount == 0 ? null : (double)entry.Value.CentipawnLossSum / entry.Value.CentipawnLossCount))
            .ToList();

        private sealed class Counts
        {
            public int Moves;
            public int FirstAttemptLegal;
            public long CentipawnLossSum;
            public int CentipawnLossCount;
        }
    }
}
