using Chess.Api.Catalog;
using Chess.Api.Contracts;
using Chess.Api.Persistence;
using Chess.Api.Usage;
using Microsoft.EntityFrameworkCore;

namespace Chess.Api.Results;

/// <summary>
/// Per-model results over decided games (03 §5.1): W/D/L, forfeit losses, move statistics over every
/// side a model played, random included, and the estimated cost of its LLM sides (03 §10.2, 03 §13).
/// </summary>
public static class ResultsBreakdown
{
    /// <param name="tournamentId">Only that tournament's games, or every decided game when null.</param>
    /// <param name="models">The catalog's players, which carry the prices the cost is estimated from.</param>
    /// <param name="usdPerAiCredit">The dollar value of one AI credit (<c>Arena:Cost:UsdPerAiCredit</c>).</param>
    /// <returns>One row per model, ordered by model id.</returns>
    public static async Task<List<ModelResultsDto>> ReadAsync(
        ArenaDbContext db, Guid? tournamentId, IReadOnlyList<ModelDto> models, double usdPerAiCredit, CancellationToken ct)
    {
        var games = ResultsGames.Decided(db);
        if (tournamentId is { } id)
        {
            games = games.Where(g => g.TournamentId == id);
        }

        var scored = await games.Select(ResultsGames.Project).ToListAsync(ct);
        var moves = await ResultsGames.MovesOf(db, games).ToListAsync(ct);
        var replies = (await ResultsGames.RepliesOf(db, games).ToListAsync(ct))
            .GroupBy(r => r.GameId)
            .ToDictionary(group => group.Key, group => group.ToList());
        var byId = scored.ToDictionary(g => g.Id);

        var totals = new Dictionary<string, Totals>(StringComparer.Ordinal);
        foreach (var game in scored)
        {
            var white = ResultsGames.WhiteScore(game.Result);
            Credit(totals, game.WhiteModel, white, game.Termination == ResultsGames.ForfeitWhite);
            Credit(totals, game.BlackModel, 1 - white, game.Termination == ResultsGames.ForfeitBlack);

            var gameReplies = replies.GetValueOrDefault(game.Id) ?? [];
            CostOf(totals, models, usdPerAiCredit, game.WhiteKind, game.WhiteModel, "white",
                gameReplies.Where(r => r.Ply % 2 == 1));
            CostOf(totals, models, usdPerAiCredit, game.BlackKind, game.BlackModel, "black",
                gameReplies.Where(r => r.Ply % 2 == 0));
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

    /// <summary>
    /// The estimated cost of one side of a game, counted for its model when it is an LLM with a known cost. A side with
    /// some replies unpriced still counts, over the replies that have a cost.
    /// </summary>
    private static void CostOf(Dictionary<string, Totals> totals, IReadOnlyList<ModelDto> models, double usdPerAiCredit,
        string kind, string model, string color, IEnumerable<ReplyRow> sideReplies)
    {
        if (kind != ResultsGames.LlmKind)
        {
            return;
        }

        // A variant (model@effort) is priced with its base model's prices (03 §10.2, ADR-012).
        var prices = models.FirstOrDefault(m => m.Id == ModelVariant.BaseOf(model))?.Pricing;
        var usage = GameCostCalculator.ForSide(color, model, isLlm: true,
            sideReplies.Select(r => new GameCostCalculator.ReplyUsage(r.InputTokens, r.OutputTokens, r.ReasoningTokens,
                r.CachedInputTokens, r.AiCredits)).ToList(),
            prices, usdPerAiCredit);
        if (usage.EstimatedUsd is { } usd)
        {
            Of(totals, model).AddCost(usd);
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
        public double CostUsdSum;
        public int CostUsdCount;

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

        public void AddCost(double usd)
        {
            CostUsdSum += usd;
            CostUsdCount++;
        }

        public ModelResultsDto ToDto(string model)
        {
            var games = Wins + Draws + Losses;
            return new ModelResultsDto(
                model,
                games,
                Wins,
                Draws,
                Losses,
                ForfeitLosses,
                Moves,
                Moves == 0 ? 0 : (double)FirstAttemptLegal / Moves,
                Moves == 0 ? 0 : (double)LatencyMsSum / Moves,
                InputTokensCount == 0 ? null : (double)InputTokensSum / InputTokensCount,
                OutputTokensCount == 0 ? null : (double)OutputTokensSum / OutputTokensCount,
                CentipawnLossCount == 0 ? null : (double)CentipawnLossSum / CentipawnLossCount,
                CostUsdCount == 0 ? null : CostUsdSum / CostUsdCount,
                games == 0 ? null : (double)ForfeitLosses / games);
        }
    }
}
