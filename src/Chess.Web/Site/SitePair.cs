using Chess.Web.Api;
using Chess.Web.Components.Pages.HeadToHead;
using Chess.Web.Components.Pages.Results;
using Chess.Web.Components.Pages.Shared;

namespace Chess.Web.Site;

/// <summary>
/// One pair of models on the head-to-head pages: the RQC of <see cref="A"/> against <see cref="B"/> (the ordinal-smaller
/// id is A), its reading and its direct games. Built from the same DTOs and rules as the interactive page.
/// </summary>
public sealed record SitePair(string A, string B, PairwiseCoefficient Cell, bool BothEstablished, IReadOnlyList<GameSummaryDto> Games)
{
    /// <summary>The sentence the page shows under the RQC (RATING.md §5), the same as the interactive page.</summary>
    public string Reading => VerbalReading.Sentence(Cell, BothEstablished);

    public bool Significant => Significance.IsSignificant(Cell, BothEstablished);

    /// <summary>The direct record in words: how many games, and what A scored per game.</summary>
    public string DirectRecord => Cell.HeadToHeadGames == 0 || Cell.HeadToHeadScore is not { } score
        ? "No direct games yet."
        : $"{Cell.HeadToHeadGames} direct {(Cell.HeadToHeadGames == 1 ? "game" : "games")}; {A} scored {ResultsModel.Number(score, "0.00")} per game.";

    public string SitePath => SitePaths.Pair(A, B);

    /// <summary>One pair per unordered pair of models, A before B in ordinal order. Needs both directions in the matrix.</summary>
    public static IReadOnlyList<SitePair> Build(MatrixDto matrix, LeaderboardDto board, IReadOnlyList<GameSummaryDto> games)
    {
        var established = board.Models;
        var pairs = new List<SitePair>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var cell in matrix.Cells)
        {
            if (string.CompareOrdinal(cell.A, cell.B) >= 0)
            {
                continue;
            }

            var path = SitePaths.Pair(cell.A, cell.B);
            if (!seen.Add(path))
            {
                throw new InvalidOperationException($"Two pairs of models would share the page {path}.");
            }

            pairs.Add(new SitePair(cell.A, cell.B, cell, Significance.BothEstablished(established, cell.A, cell.B),
                DirectGames.Between(games, cell.A, cell.B)));
        }

        return pairs.OrderBy(p => p.A, StringComparer.Ordinal).ThenBy(p => p.B, StringComparer.Ordinal).ToList();
    }
}
