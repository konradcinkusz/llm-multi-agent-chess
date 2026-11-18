using System.Net;
using System.Text;
using Chess.Web.Api;
using Chess.Web.Components.Shared.Game;
using Chess.Web.Components.Site.Game;

namespace Chess.Web.Site.Game;

/// <summary>
/// Writes the page and the PGN of every public game (03 §11): <c>games/&lt;id&gt;/index.html</c> and
/// <c>games/&lt;id&gt;/game.pgn</c>. The game and its PGN come from the API, the usage from the snapshot, and the engine log
/// is read only for the games that keep it. A game the API has no PGN for (409) gets its page without a PGN file and without
/// the Download link; any other failure stops the export and names the game.
/// </summary>
internal sealed class SiteGamePages(ArenaApiClient api)
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public async Task WriteAsync(SiteOutput output, SiteRenderer renderer, Func<int, SiteFrame> frame, SiteSnapshot snapshot,
        int fullLogGames, CancellationToken ct)
    {
        // The snapshot lists the games newest first, so the index decides which of them keep the engine log.
        for (var index = 0; index < snapshot.Games.Count; index++)
        {
            var summary = snapshot.Games[index];
            try
            {
                await WriteGameAsync(output, renderer, frame, snapshot, summary, SiteGameRules.KeepsEngineLog(index, fullLogGames), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new InvalidOperationException(
                    $"Game {summary.Id} ({summary.White.ModelId} against {summary.Black.ModelId}) could not be exported: {ex.Message}", ex);
            }
        }
    }

    private async Task WriteGameAsync(SiteOutput output, SiteRenderer renderer, Func<int, SiteFrame> frame, SiteSnapshot snapshot,
        GameSummaryDto summary, bool keepsLog, CancellationToken ct)
    {
        var game = await api.GetGameAsync(summary.Id, ct);
        IReadOnlyList<EngineEntry> attempts = keepsLog ? await ReadAttemptsAsync(summary.Id, ct) : [];
        var pgn = await ReadPgnAsync(summary.Id, ct);
        var tournamentListed = summary.TournamentId is { } id && snapshot.Tournaments.Any(t => t.Id == id);

        var html = await renderer.RenderAsync<SiteGamePage>(("Frame", frame(2)), ("Game", game),
            ("Usage", snapshot.Usage.GetValueOrDefault(summary.Id)), ("Attempts", attempts), ("KeepsLog", keepsLog),
            ("HasPgn", pgn is not null), ("TournamentListed", tournamentListed));
        await output.WritePageAsync(SitePaths.Game(summary.Id), html, ct);
        if (pgn is not null)
        {
            await output.WriteBytesAsync(PgnPath(summary.Id), Utf8.GetBytes(pgn), ct);
        }
    }

    /// <summary>The PGN of the game, or null when the API has none (409: the game never produced one).</summary>
    private async Task<string?> ReadPgnAsync(Guid id, CancellationToken ct)
    {
        try
        {
            return await api.GetGamePgnAsync(id, ct);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            return null;
        }
    }

    /// <summary>The PGN file of a game, in the folder of its page.</summary>
    public static string PgnPath(Guid id) => $"games/{id:D}/{SiteGameRules.PgnFileName}";

    /// <summary>Every attempt of the game, page by page, in the order the API gives them.</summary>
    private async Task<IReadOnlyList<EngineEntry>> ReadAttemptsAsync(Guid gameId, CancellationToken ct)
    {
        var entries = new List<EngineEntry>();
        for (var page = 1; ; page++)
        {
            var result = await api.GetAttemptsAsync(gameId, null, page, ct);
            entries.AddRange(result.Items.Select(EngineEntry.From));
            if (result.Items.Count == 0 || page * result.PageSize >= result.Total)
            {
                return entries;
            }
        }
    }
}
