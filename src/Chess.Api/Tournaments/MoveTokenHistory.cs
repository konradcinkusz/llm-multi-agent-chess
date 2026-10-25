using Chess.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Chess.Api.Tournaments;

/// <summary>A model's average tokens per move, over the stored moves that reported both counts.</summary>
public sealed record ModelTokenAverage(int Moves, double AverageInputTokens, double AverageOutputTokens);

/// <summary>Reads the per-model token averages that the cost estimates use (03 §10.2: from <c>moves.input_tokens</c> and <c>output_tokens</c>).</summary>
public static class MoveTokenHistory
{
    /// <summary>
    /// Per model, the average over every stored move that reported input and output tokens, whichever side it played.
    /// A model with no such move is absent. Random moves report no tokens, so the random player is never listed.
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, ModelTokenAverage>> ReadAsync(ArenaDbContext db, CancellationToken ct)
    {
        var rows = await db.Moves.AsNoTracking()
            .Where(move => move.InputTokens != null && move.OutputTokens != null)
            .Join(db.Games.AsNoTracking(), move => move.GameId, game => game.Id,
                (move, game) => new
                {
                    move.Color,
                    game.WhiteModel,
                    game.BlackModel,
                    move.InputTokens,
                    move.OutputTokens,
                })
            .GroupBy(row => row.Color == "white" ? row.WhiteModel : row.BlackModel)
            .Select(group => new
            {
                Model = group.Key,
                Moves = group.Count(),
                Input = group.Average(row => (double?)row.InputTokens),
                Output = group.Average(row => (double?)row.OutputTokens),
            })
            .ToListAsync(ct);

        return rows.ToDictionary(row => row.Model,
            row => new ModelTokenAverage(row.Moves, row.Input ?? 0, row.Output ?? 0), StringComparer.Ordinal);
    }
}
