using Chess.Web.Api;
using Chess.Web.Components.Pages.Games;
using Chess.Web.Components.Pages.Results;
using Chess.Web.Components.Site;
using Chess.Web.Site.Game;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;

namespace Chess.Web.Site;

/// <summary>
/// Renders the public results site (03 §11, ADR-013) as static files: every page, including the per-game pages, the assets,
/// the JSON data and <c>.nojekyll</c>. It reads through the API like the interactive pages do, and writes nothing else.
/// </summary>
public sealed class SiteExporter(
    ArenaApiClient api,
    IHttpClientFactory httpClients,
    IWebHostEnvironment environment,
    IOptions<SiteOptions> options,
    TimeProvider clock,
    IServiceScopeFactory scopes,
    ILoggerFactory loggerFactory) : ISiteExporter
{
    /// <summary>The assets copied into <c>assets/</c>: the stylesheets of the shell and the chart, and the favicon.</summary>
    private static readonly string[] AssetFiles = ["app.css", "Chess.Web.styles.css", "favicon.png"];

    public async Task<SiteExportResult> ExportAsync(string outputDirectory, CancellationToken ct)
    {
        var root = Path.GetFullPath(outputDirectory);
        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
        {
            // A folder with old files would publish pages the export no longer makes (for example a deleted game).
            throw new InvalidOperationException($"The export folder {root} is not empty. Export into an empty folder.");
        }

        Directory.CreateDirectory(root);
        var takenAt = clock.GetUtcNow();
        var ratings = new TournamentRatingsClient(httpClients.CreateClient(TournamentRatingsClient.ClientName));
        var snapshot = await new SiteLoader(api, ratings).LoadAsync(takenAt, ct);

        var output = new SiteOutput(root);

        // The renderer resolves scoped services, so the export runs in a scope of its own.
        await using var scope = scopes.CreateAsyncScope();
        await using var renderer = new SiteRenderer(scope.ServiceProvider, loggerFactory);
        var frame = FrameFor(options.Value, snapshot);

        var report = ResultsModel.Build(ResultsInputsFor(snapshot));
        var pairs = SitePair.Build(snapshot.Matrix, snapshot.Board, snapshot.Games);

        await output.WritePageAsync(SitePaths.Results,
            await renderer.RenderAsync<SiteResultsPage>(("Frame", frame(0)), ("Board", snapshot.Board), ("Report", report)), ct);
        await output.WritePageAsync(SitePaths.HeadToHead,
            await renderer.RenderAsync<SiteHeadToHeadPage>(("Frame", frame(1)), ("Report", report), ("Pairs", pairs)), ct);
        foreach (var pair in pairs)
        {
            await output.WritePageAsync(pair.SitePath,
                await renderer.RenderAsync<SiteHeadToHeadPairPage>(("Frame", frame(2)), ("Pair", pair)), ct);
        }

        await WriteGamesAsync(output, renderer, frame, snapshot, ct);
        await WriteTournamentsAsync(output, renderer, frame, snapshot, ct);
        await new SiteGamePages(api).WriteAsync(output, renderer, frame, snapshot, options.Value.FullLogGames, ct);

        await output.WritePageAsync(SitePaths.Menu,
            await renderer.RenderAsync<SiteMenuPage>(("Frame", frame(1)), ("PriceMenu", snapshot.PriceMenu)), ct);
        await output.WritePageAsync(SitePaths.About, await renderer.RenderAsync<SiteAboutPage>(("Frame", frame(1))), ct);

        await WriteDataAsync(output, snapshot, ct);
        await CopyAssetsAsync(output, ct);
        await output.WriteBytesAsync(".nojekyll", [], ct);

        return new SiteExportResult(output.Pages, snapshot.Games.Count, output.Bytes, takenAt);
    }

    /// <summary>The frame of a page at <c>depth</c>: the snapshot time, the rated games and the links the configuration allows.</summary>
    private static Func<int, SiteFrame> FrameFor(SiteOptions options, SiteSnapshot snapshot)
    {
        var watchLive = SiteLinks.Safe(options.YouTubeUrl);
        var support = SiteLinks.Support(options);
        return depth => new SiteFrame(depth, snapshot.TakenAt, snapshot.Board.Games, watchLive, support);
    }

    private static ResultsInputs ResultsInputsFor(SiteSnapshot snapshot)
    {
        var models = snapshot.Board.Models.Select(m => m.Model).ToList();
        var leaderVsSecond = models.Count > 1
            ? snapshot.Matrix.Cells.FirstOrDefault(c => c.A == models[0] && c.B == models[1])
            : null;
        return new ResultsInputs(snapshot.Board, models, snapshot.Matrix, snapshot.Breakdown, leaderVsSecond,
            snapshot.History);
    }

    private static async Task WriteGamesAsync(SiteOutput output, SiteRenderer renderer, Func<int, SiteFrame> frame,
        SiteSnapshot snapshot, CancellationToken ct)
    {
        var pageCount = GameArchive.PageCount(snapshot.Games.Count);
        for (var page = 1; page <= pageCount; page++)
        {
            var rows = SiteGames.Page(snapshot.Games, page)
                .Select(game => new SiteGameRow(game, snapshot.Usage.GetValueOrDefault(game.Id)))
                .ToList();
            var depth = page == 1 ? 1 : 2;
            await output.WritePageAsync(SitePaths.GamesPage(page), await renderer.RenderAsync<SiteGamesPage>(
                ("Frame", frame(depth)), ("Rows", rows), ("Page", page), ("PageCount", pageCount), ("Total", snapshot.Games.Count)), ct);
        }
    }

    private static async Task WriteTournamentsAsync(SiteOutput output, SiteRenderer renderer, Func<int, SiteFrame> frame,
        SiteSnapshot snapshot, CancellationToken ct)
    {
        var tournaments = snapshot.Tournaments.OrderByDescending(t => t.CreatedAt).ThenBy(t => t.Id).ToList();
        await output.WritePageAsync(SitePaths.Tournaments,
            await renderer.RenderAsync<SiteTournamentsPage>(("Frame", frame(1)), ("Tournaments", tournaments)), ct);

        foreach (var tournament in tournaments)
        {
            var games = snapshot.Games
                .Where(game => game.TournamentId == tournament.Id)
                .OrderBy(game => game.CreatedAt)
                .ThenBy(game => game.Id)
                .ToList();
            await output.WritePageAsync(SitePaths.Tournament(tournament.Id), await renderer.RenderAsync<SiteTournamentPage>(
                ("Frame", frame(2)), ("Tournament", tournament), ("Board", snapshot.TournamentBoards[tournament.Id]), ("Games", games)), ct);
        }
    }

    private static async Task WriteDataAsync(SiteOutput output, SiteSnapshot snapshot, CancellationToken ct)
    {
        await output.WriteJsonAsync("data/snapshot.json", new SiteSnapshotJson(snapshot.TakenAt, snapshot.Board.Games,
            snapshot.Board.Models.Count, snapshot.Tournaments.Count, snapshot.Games.Count), ct);
        await output.WriteJsonAsync("data/leaderboard.json", snapshot.Board, ct);
        await output.WriteJsonAsync("data/matrix.json", snapshot.Matrix, ct);
        await output.WriteJsonAsync("data/history.json", snapshot.History, ct);
        await output.WriteJsonAsync("data/breakdown.json", snapshot.Breakdown, ct);
        await output.WriteJsonAsync("data/tournaments.json", snapshot.Tournaments, ct);
        await output.WriteJsonAsync("data/games.json", snapshot.Games, ct);
        await output.WriteJsonAsync("data/usage.json", snapshot.Usage.Values.ToList(), ct);
    }

    private async Task CopyAssetsAsync(SiteOutput output, CancellationToken ct)
    {
        foreach (var name in AssetFiles)
        {
            var file = environment.WebRootFileProvider.GetFileInfo(name);
            if (!file.Exists)
            {
                // Without the stylesheets the pages would be unstyled, so a missing asset fails the export.
                throw new FileNotFoundException($"The site asset {name} is not in the web root.", name);
            }

            await using var source = file.CreateReadStream();
            await output.CopyAsync($"assets/{name}", source, ct);
        }
    }
}
