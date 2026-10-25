using System.Text.RegularExpressions;

namespace Chess.Web.Site.Publishing;

/// <summary>Where the site goes: the remote to push to, the public address and the problem, if there is none.</summary>
public sealed record SiteLocation(string? Remote, string? Url, string? Problem);

/// <summary>Finds the remote to publish to, and derives the public address from a GitHub remote.</summary>
public static partial class GitRemote
{
    public static async Task<SiteLocation> LocateAsync(SitePublishingOptions options, string sourceDirectory, CancellationToken ct)
    {
        var (remote, problem) = await ResolveAsync(options, sourceDirectory, ct);
        if (remote is null)
        {
            return new SiteLocation(null, options.PublicUrl, problem);
        }
        return new SiteLocation(Redact(remote), PagesUrl(remote, options.PublicUrl), null);
    }

    /// <summary>
    /// The configured remote, else the origin of the repository the app runs from. A problem text (git missing, no
    /// origin) when there is none.
    /// </summary>
    public static async Task<(string? Remote, string? Problem)> ResolveAsync(SitePublishingOptions options, string sourceDirectory, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(options.Remote))
        {
            return (options.Remote.Trim(), null);
        }

        var origin = await GitProcess.RunAsync(options.GitExecutable, sourceDirectory, ct, "config", "--get", "remote.origin.url");
        if (origin.ExitCode < 0)
        {
            return (null, origin.Message);
        }
        if (!origin.Ok || origin.Stdout.Length == 0)
        {
            return (null, "No remote: the repository the app runs from has no origin, and Arena:Site:Remote is not set.");
        }
        return (origin.Stdout, null);
    }

    /// <summary>The public address of a GitHub Pages site for this remote, or the configured one. Null for other hosts.</summary>
    public static string? PagesUrl(string remote, string? publicUrl)
    {
        if (!string.IsNullOrWhiteSpace(publicUrl))
        {
            return publicUrl;
        }

        var match = GitHubRemote().Match(remote.Trim());
        if (!match.Success)
        {
            return null;
        }

        var owner = match.Groups["owner"].Value.ToLowerInvariant();
        var repo = match.Groups["repo"].Value;
        return repo.Equals($"{owner}.github.io", StringComparison.OrdinalIgnoreCase)
            ? $"https://{owner}.github.io/"
            : $"https://{owner}.github.io/{repo}/";
    }

    /// <summary>The remote without user information (a token in the URL must never reach the page or a log).</summary>
    public static string Redact(string remote)
    {
        if (Uri.TryCreate(remote, UriKind.Absolute, out var uri) && uri.UserInfo.Length > 0)
        {
            return new UriBuilder(uri) { UserName = string.Empty, Password = string.Empty }.Uri.ToString();
        }
        return remote;
    }

    [GeneratedRegex(@"^(?:https://(?:[^@/]+@)?github\.com/|git@github\.com:|ssh://git@github\.com/)(?<owner>[^/]+)/(?<repo>[^/]+?)(?:\.git)?/?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GitHubRemote();
}
