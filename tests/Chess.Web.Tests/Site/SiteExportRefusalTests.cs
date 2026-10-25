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
}
