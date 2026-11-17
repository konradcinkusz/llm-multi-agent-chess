extern alias ChessWeb;

using Chess.Web.Site;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Chess.Web.Tests.Site;

/// <summary>An export never writes into a folder that holds old files: a page the new export no longer makes would stay published.</summary>
public class SiteExportRefusalTests
{
    [Fact]
    public async Task A_folder_with_files_in_it_is_refused_and_left_as_it_was()
    {
        await using var app = new SiteExportApp();
        var directory = Path.Combine(Path.GetTempPath(), "chess-site-refuse-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var stale = Path.Combine(directory, "games-old.html");
        await File.WriteAllTextAsync(stale, "stale");

        try
        {
            var exporter = app.Services.GetRequiredService<ISiteExporter>();

            await Assert.ThrowsAsync<InvalidOperationException>(() => exporter.ExportAsync(directory, CancellationToken.None));

            Assert.Equal(["games-old.html"], Directory.EnumerateFiles(directory).Select(Path.GetFileName));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task A_failure_while_exporting_a_game_names_the_game()
    {
        await using var app = new SiteExportApp
        {
            Override = request => request.Target.EndsWith($"{SiteDataSet.GameId(3)}/pgn", StringComparison.Ordinal)
                ? new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError)
                : null,
        };
        var directory = Path.Combine(Path.GetTempPath(), "chess-site-fail-" + Guid.NewGuid().ToString("N"));

        try
        {
            var exporter = app.Services.GetRequiredService<ISiteExporter>();

            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => exporter.ExportAsync(directory, CancellationToken.None));

            Assert.Contains(SiteDataSet.GameId(3).ToString("D"), failure.Message, StringComparison.Ordinal);
            Assert.Contains("500", failure.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Games_that_finished_undecided_are_never_asked_for_a_pgn_a_page_or_a_usage()
    {
        await using var app = new SiteExportApp();
        var (directory, _) = await app.ExportAsync();

        try
        {
            foreach (var undecided in new[] { SiteDataSet.AbortedGame, SiteDataSet.RestartedGame })
            {
                Assert.DoesNotContain(app.Api.Requests, request => request.Target.Contains(undecided.ToString("D"), StringComparison.Ordinal));
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
