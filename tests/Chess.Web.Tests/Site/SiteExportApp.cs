extern alias ChessWeb;

using System.Net;
using System.Text.RegularExpressions;
using Chess.Web.Api;
using Chess.Web.Site;
using Chess.Web.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Chess.Web.Tests.Site;

/// <summary>
/// The real web app with its API answering from <see cref="SiteDataSet"/>, a fixed clock and the given configuration. The
/// export runs through the registered <see cref="ISiteExporter"/>, so the tests cover the registration as well.
/// </summary>
internal sealed class SiteExportApp(bool empty = false, params (string Key, string Value)[] settings) : WebApplicationFactory<ChessWeb::Program>
{
    public FakeHandler Api { get; } = new(request => SiteDataSet.Respond(request, empty));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        foreach (var (key, value) in settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
        {
            services.AddHttpClient<ArenaApiClient>(client => client.BaseAddress = new Uri("http://api"))
                .ConfigurePrimaryHttpMessageHandler(() => Api);
            services.AddSingleton<TimeProvider>(new FixedClock(SiteDataSet.Now));
        });
    }

    /// <summary>Exports into a new empty folder and returns the folder with the result. The folder is the caller's to delete.</summary>
    public async Task<(string Directory, SiteExportResult Result)> ExportAsync(CancellationToken ct = default)
    {
        var directory = Path.Combine(Path.GetTempPath(), "chess-site-" + Guid.NewGuid().ToString("N"));
        var exporter = Services.GetRequiredService<ISiteExporter>();
        var result = await exporter.ExportAsync(directory, ct);
        return (directory, result);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

/// <summary>Exports the default data set once for a test class, and deletes the folder afterwards.</summary>
public class ExportedSite : IAsyncLifetime
{
    public string Directory { get; private set; } = "";

    public SiteExportResult Result { get; private set; } = null!;

    public Dictionary<string, string> Pages { get; } = new(StringComparer.Ordinal);

    public virtual Task InitializeAsync() => ExportWith();

    public async Task ExportWith(bool empty = false, params (string Key, string Value)[] settings)
    {
        await DisposeAsync();
        await using var app = new SiteExportApp(empty, settings);
        (Directory, Result) = await app.ExportAsync();
        Pages.Clear();
        foreach (var file in System.IO.Directory.EnumerateFiles(Directory, "*.html", SearchOption.AllDirectories))
        {
            Pages[Path.GetRelativePath(Directory, file).Replace(Path.DirectorySeparatorChar, '/')] = await File.ReadAllTextAsync(file);
        }
    }

    public Task DisposeAsync()
    {
        if (System.IO.Directory.Exists(Directory))
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }

        return Task.CompletedTask;
    }
}

/// <summary>The link check over an export: every <c>href</c> and <c>src</c> is relative and resolves to a file in the export.</summary>
internal static class LinkCheck
{
    private static readonly Regex Attribute = new(@"\b(?:href|src)=""([^""]*)""", RegexOptions.Compiled);

    /// <summary>The per-game pages (S2) are linked from the games list before they exist; only their links are allowed to be missing.</summary>
    private static readonly Regex GamePage = new(@"^games/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/index\.html$", RegexOptions.Compiled);

    /// <summary>Every link of every page, as "page → target".</summary>
    public static IEnumerable<(string Page, string Target)> LinksOf(IReadOnlyDictionary<string, string> pages) =>
        pages.SelectMany(page => Attribute.Matches(page.Value)
            .Select(match => (page.Key, WebUtility.HtmlDecode(match.Groups[1].Value))));

    /// <summary>Links that are absolute, leave the export, or point at a missing file (game pages excepted). Empty when the export is sound.</summary>
    public static List<string> Problems(string directory, IReadOnlyDictionary<string, string> pages, ISet<string> allowedAbsolute)
    {
        var root = Path.GetFullPath(directory);
        var problems = new List<string>();
        foreach (var (page, target) in LinksOf(pages))
        {
            if (allowedAbsolute.Contains(target))
            {
                continue;
            }

            if (target.Length == 0 || target.Contains("://", StringComparison.Ordinal) || target.StartsWith('/')
                || target.StartsWith("mailto:", StringComparison.Ordinal) || target.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
            {
                problems.Add($"{page}: not a relative link: {target}");
                continue;
            }

            var pathPart = target.Split('?', '#')[0];
            var resolved = Path.GetFullPath(Path.Combine(root, Path.GetDirectoryName(page)!, pathPart));
            if (!resolved.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                problems.Add($"{page}: leaves the export: {target}");
                continue;
            }

            var relative = Path.GetRelativePath(root, resolved).Replace(Path.DirectorySeparatorChar, '/');
            var exists = File.Exists(resolved) || System.IO.Directory.Exists(resolved);
            if (!exists && !GamePage.IsMatch(relative))
            {
                problems.Add($"{page}: missing {relative}");
            }
        }

        return problems;
    }
}
