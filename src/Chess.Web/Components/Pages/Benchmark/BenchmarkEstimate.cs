using Chess.Web.Api;
using Chess.Web.Components.Pages.Tournaments;

namespace Chess.Web.Components.Pages.Benchmark;

/// <summary>
/// The cost of a benchmark before it starts (03 §10.2, ADR-014): the candidate plays every opponent, each opponent plays its
/// own games against the candidate. Each side makes <see cref="CostPreview.MovesPerSidePerGame"/> moves a game, as the
/// tournament estimate does, so the two forms agree on what a game costs.
/// </summary>
public static class BenchmarkEstimate
{
    /// <summary>A run this many games or longer is marked large in the form (ADR-014: cost scales with opponents × games).</summary>
    public const int LargeGames = 150;

    /// <summary>A run whose known USD estimate reaches this is marked large in the form.</summary>
    public const double LargeUsd = 5;

    /// <summary>One player's share of the run. Null figures are unknown: a missing price or multiplier, not zero.</summary>
    public sealed record Row(string Player, string Role, int Games, double? PremiumRequests, double? AiCredits, double? Usd,
        string Basis, int? SampleMoves);

    /// <summary>
    /// The run's totals. Each is the sum of the known rows, so it is a lower bound when a count of unknown rows is above zero.
    /// </summary>
    public sealed record Totals(int Games, double PremiumRequests, int UnknownPremium, double AiCredits, double Usd, int UnknownUsd);

    public sealed record Result(IReadOnlyList<Row> Rows, Totals Totals);

    /// <param name="candidate">The candidate's model, with its token prices.</param>
    /// <param name="candidatePlayer">The candidate's player id, with its effort (the id its tokens are priced under).</param>
    /// <param name="opponents">The opponents, as the API will play them.</param>
    /// <param name="costs">The rate and token averages from <c>GET /api/cost/inputs</c>; null when they could not be loaded.</param>
    /// <param name="gamesPerOpponent">The games each opponent plays against the candidate.</param>
    public static Result For(ModelDto candidate, string candidatePlayer, IReadOnlyList<ModelDto> opponents, CostInputsDto? costs,
        int gamesPerOpponent)
    {
        var rows = new List<Row>();
        var candidateGames = opponents.Count * gamesPerOpponent;
        rows.Add(RowOf(candidate with { Id = candidatePlayer }, "candidate", candidateGames, costs));

        foreach (var opponent in opponents)
        {
            rows.Add(RowOf(opponent, "opponent", gamesPerOpponent, costs));
        }

        var totals = new Totals(
            Games: opponents.Count * gamesPerOpponent,
            PremiumRequests: rows.Sum(row => row.PremiumRequests ?? 0),
            UnknownPremium: rows.Count(row => row.PremiumRequests is null),
            AiCredits: rows.Sum(row => row.AiCredits ?? 0),
            Usd: rows.Sum(row => row.Usd ?? 0),
            UnknownUsd: rows.Count(row => row.Usd is null));
        return new Result(rows, totals);
    }

    /// <summary>Large when the run has many games, or when its known USD total reaches <see cref="LargeUsd"/>.</summary>
    public static bool IsLarge(Totals totals) =>
        totals.Games >= LargeGames || totals.Usd >= LargeUsd;

    private static Row RowOf(ModelDto model, string role, int games, CostInputsDto? costs)
    {
        var moves = games * CostPreview.MovesPerSidePerGame;
        var cost = costs is null ? null : CostPreview.For(model, costs, moves);
        var premium = model.PremiumMultiplier is { } multiplier ? games * PremiumEstimate.RequestsPerGame * multiplier : (double?)null;
        return new Row(model.Id, role, games, premium, cost?.AiCredits, cost?.Usd, cost?.Basis ?? "unknown", cost?.SampleMoves);
    }
}
