using System.Text;
using Chess.Web.Api;
using Chess.Web.Components.Shared.Game;
using Chess.Web.Components.Site.Game;

namespace Chess.Web.Site.Game;

/// <summary>
/// Writes the page and the PGN of every finished game (03 §11): <c>games/&lt;id&gt;/index.html</c> and
/// <c>games/&lt;id&gt;/game.pgn</c>. The game and its PGN come from the API, the usage from the snapshot, and the engine log
/// is read only for the games that keep it.
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
            var game = await api.GetGameAsync(summary.Id, ct);
            var keepsLog = SiteGameRules.KeepsEngineLog(index, fullLogGames);
            IReadOnlyList<EngineEntry> attempts = keepsLog ? await ReadAttemptsAsync(summary.Id, ct) : [];

            var html = await renderer.RenderAsync<SiteGamePage>(("Frame", frame(2)), ("Game", game),
                ("Usage", snapshot.Usage.GetValueOrDefault(summary.Id)), ("Attempts", attempts), ("KeepsLog", keepsLog));
            await output.WritePageAsync(SitePaths.Game(summary.Id), html, ct);
            await output.WriteBytesAsync(PgnPath(summary.Id), Utf8.GetBytes(await api.GetGamePgnAsync(summary.Id, ct)), ct);
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
