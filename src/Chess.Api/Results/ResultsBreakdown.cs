using Chess.Api.Contracts;
using Chess.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Chess.Api.Results;

/// <summary>
/// Per-model results over decided games (03 §5.1): W/D/L, forfeit losses, and move statistics over every
/// side a model played, random included.
/// </summary>
public static class ResultsBreakdown
{
    /// <param name="tournamentId">Only that tournament's games, or every decided game when null.</param>
    /// <returns>One row per model, ordered by model id.</returns>
    public static async Task<List<ModelResultsDto>> ReadAsync(ArenaDbContext db, Guid? tournamentId, CancellationToken ct)
    {
        var games = ResultsGames.Decided(db);
        if (tournamentId is { } id)
        {
            games = games.Where(g => g.TournamentId == id);
        }

        var scored = await games.Select(ResultsGames.Project).ToListAsync(ct);
        var moves = await ResultsGames.MovesOf(db, games).ToListAsync(ct);
        var byId = scored.ToDictionary(g => g.Id);

        var totals = new Dictionary<string, Totals>(StringComparer.Ordinal);
        foreach (var game in scored)
        {
            var white = ResultsGames.WhiteScore(game.Result);
            Credit(totals, game.WhiteModel, white, game.Termination == ResultsGames.ForfeitWhite);
            Credit(totals, game.BlackModel, 1 - white, game.Termination == ResultsGames.ForfeitBlack);
        }

        foreach (var move in moves)
        {
            // A move whose game was decided after the game query ran is left for the next read.
            if (!byId.TryGetValue(move.GameId, out var game))
            {
                continue;
            }

            var (_, model) = ResultsGames.Mover(game, move.FenAfter);
            Of(totals, model).AddMove(move);
        }

        return totals
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => entry.Value.ToDto(entry.Key))
            .ToList();
    }

    private static void Credit(Dictionary<string, Totals> totals, string model, double score, bool forfeited)
    {
        var entry = Of(totals, model);
        if (score == 1)
        {
            entry.Wins++;
        }
        else if (score == 0.5)
        {
            entry.Draws++;
        }
        else
        {
            entry.Losses++;
            if (forfeited)
            {
                entry.ForfeitLosses++;
            }
        }
    }

    private static Totals Of(Dictionary<string, Totals> totals, string model)
    {
        if (!totals.TryGetValue(model, out var entry))
        {
            totals[model] = entry = new Totals();
        }

        return entry;
    }

    private sealed class Totals
    {
        public int Wins;
        public int Draws;
        public int Losses;
        public int ForfeitLosses;
        public int Moves;
        public int FirstAttemptLegal;
        public long LatencyMsSum;
        public long InputTokensSum;
        public int InputTokensCount;
        public long OutputTokensSum;
        public int OutputTokensCount;
        public long CentipawnLossSum;
        public int CentipawnLossCount;

        public void AddMove(MoveRow move)
        {
            Moves++;
            if (move.Attempts == 1)
            {
                FirstAttemptLegal++;
            }

            LatencyMsSum += move.LatencyMs;
            if (move.InputTokens is { } input)
            {
                InputTokensSum += input;
                InputTokensCount++;
            }

            if (move.OutputTokens is { } output)
            {
                OutputTokensSum += output;
                OutputTokensCount++;
            }

            if (move.CentipawnLoss is { } loss)
            {
                CentipawnLossSum += loss;
                CentipawnLossCount++;
            }
        }

        public ModelResultsDto ToDto(string model) => new(
            model,
            Wins + Draws + Losses,
            Wins,
            Draws,
            Losses,
            ForfeitLosses,
            Moves,
            Moves == 0 ? 0 : (double)FirstAttemptLegal / Moves,
            Moves == 0 ? 0 : (double)LatencyMsSum / Moves,
            InputTokensCount == 0 ? null : (double)InputTokensSum / InputTokensCount,
            OutputTokensCount == 0 ? null : (double)OutputTokensSum / OutputTokensCount,
            CentipawnLossCount == 0 ? null : (double)CentipawnLossSum / CentipawnLossCount);
    }
}
