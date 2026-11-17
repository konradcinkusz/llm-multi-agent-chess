using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Chess.Web.Site.Publishing;

/// <summary>
/// 03 §11 and ADR-013 (an advanced option: GitHub Pages is deployed from the publish-site workflow, and this publisher does
/// not serve the public URL): replaces the content of one branch with one orphan commit of the export, force-pushed. It works
/// in its own folder (Arena:Site:WorkDir), never in the owner's working tree, so `main` and the working tree stay untouched.
/// Because it force-pushes, it refuses a branch it must not overwrite: the remote's default branch, <c>main</c> and
/// <c>master</c>, and any branch whose tip this publisher did not author. Every expected failure is reported in the result
/// with a detail; nothing is thrown for it.
/// </summary>
public sealed partial class GitSitePublisher(IOptions<SitePublishingOptions> options, TimeProvider time, string sourceDirectory) : ISitePublisher
{
    private const string MarkerName = "arena-site-publisher";

    /// <summary>The identity of every commit this publisher makes. A branch whose tip has another author is not ours to overwrite.</summary>
    public const string AuthorName = "llm-chess-arena";

    public const string AuthorEmail = "site@llm-chess-arena.invalid";

    private static readonly string[] ProtectedBranches = ["main", "master", "HEAD"];

    public async Task<SitePublishResult> PublishAsync(string siteDirectory, CancellationToken ct)
    {
        try
        {
            return await PublishCoreAsync(siteDirectory, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Refused($"Publishing failed: {ex.Message}");
        }
    }

    private async Task<SitePublishResult> PublishCoreAsync(string siteDirectory, CancellationToken ct)
    {
        var opts = options.Value;
        if (!File.Exists(Path.Combine(siteDirectory, "index.html")))
        {
            return Refused("The export has no index.html, so nothing was published.");
        }

        if (BranchProblem(opts.Branch) is { } branchProblem)
        {
            return Refused(branchProblem);
        }

        var (remote, problem) = await GitRemote.ResolveAsync(opts, sourceDirectory, ct);
        if (remote is null)
        {
            return Refused(problem!);
        }
        if (remote.StartsWith('-'))
        {
            return Refused("The remote must be a URL or a path, not an option.");
        }

        var workDir = opts.ResolvedWorkDir;
        var roots = await SiteFolders.AppRootsAsync(opts.GitExecutable, sourceDirectory, ct);
        if (roots.Any(root => SiteFolders.IsInsideOrAbove(workDir, root)))
        {
            return Refused("Arena:Site:WorkDir must be a folder of its own, not the repository the app runs from.");
        }
        if (SiteFolders.IsInsideOrAbove(workDir, Path.GetFullPath(siteDirectory)))
        {
            return Refused("Arena:Site:WorkDir and the export folder must be two separate folders, neither inside the other.");
        }

        var git = opts.GitExecutable;
        var shown = GitRemote.Redact(remote);
        var remoteState = await ReadRemoteAsync(git, workDir, remote, opts.Branch, ct);
        if (remoteState.Problem is { } remoteProblem)
        {
            return Refused($"The push to {shown} was refused: {remoteProblem}");
        }
        if (remoteState.DefaultBranch == opts.Branch)
        {
            return Refused($"Arena:Site:Branch ({opts.Branch}) is the default branch of {shown}. The publisher force-pushes, so it never writes the default branch; choose another branch.");
        }

        if (!TryPrepareWorkDir(workDir, out var prepareProblem))
        {
            return Refused(prepareProblem);
        }

        var step = await Git(git, workDir, ct, "init", "--quiet");
        if (step is not null) return Refused($"git init failed: {step}");
        step = await Git(git, workDir, ct, "symbolic-ref", "HEAD", $"refs/heads/{opts.Branch}");
        if (step is not null) return Refused($"git could not name the branch {opts.Branch}: {step}");
        await File.WriteAllTextAsync(Path.Combine(workDir, ".git", MarkerName), "publisher folder\n", ct);

        if (remoteState.Tip is not null)
        {
            var foreign = await ForeignTipAsync(git, workDir, remote, opts.Branch, ct);
            if (foreign is not null)
            {
                return Refused($"The branch {opts.Branch} of {shown} was not left by this publisher ({foreign}), so it was not overwritten. Choose another Arena:Site:Branch or delete that branch yourself.");
            }
        }

        var (files, bytes) = CopyTree(siteDirectory, workDir);
        await File.WriteAllTextAsync(Path.Combine(workDir, ".nojekyll"), string.Empty, ct);

        step = await Git(git, workDir, ct, "add", "--all", "--force");
        if (step is not null) return Refused($"git add failed: {step}");
        var at = time.GetUtcNow();
        step = await Git(git, workDir, ct, "-c", $"user.name={AuthorName}", "-c", $"user.email={AuthorEmail}", "-c", "commit.gpgSign=false",
            "-c", $"core.hooksPath={Path.Combine(workDir, ".git", "hooks")}",
            "commit", "--quiet", "--no-verify", "-m", $"Publish results site {at:yyyy-MM-dd HH:mm} UTC");
        if (step is not null) return Refused($"git commit failed: {step}");

        var head = await GitProcess.RunAsync(git, workDir, ct, "rev-parse", "HEAD");
        if (!head.Ok) return Refused($"git rev-parse failed: {head.Message}");
        var commit = head.Stdout;

        // With a tip to replace, the push is a lease on exactly that tip: it fails instead of overwriting a commit that
        // arrived after it was read.
        var force = remoteState.Tip is { } known ? $"--force-with-lease=refs/heads/{opts.Branch}:{known}" : "--force";
        step = await Git(git, workDir, ct, "push", force, "--quiet", "--", remote, $"HEAD:refs/heads/{opts.Branch}");
        if (step is not null) return Refused($"The push to {shown} was refused: {step}");

        return new SitePublishResult(true, commit,
            $"Published {files} files ({bytes:N0} bytes) to {opts.Branch} of {shown} as commit {commit[..7]}.", at);
    }

    /// <summary>The reason the branch name cannot be force-pushed to, or null.</summary>
    private static string? BranchProblem(string branch)
    {
        if (string.IsNullOrWhiteSpace(branch) || !BranchName().IsMatch(branch) || branch.Contains("..", StringComparison.Ordinal)
            || branch.EndsWith('/') || branch.EndsWith(".lock", StringComparison.Ordinal))
        {
            return $"Arena:Site:Branch '{branch}' is not a plain branch name (letters, digits, '.', '_', '-' and '/').";
        }

        return ProtectedBranches.Contains(branch, StringComparer.OrdinalIgnoreCase)
            ? $"Arena:Site:Branch ({branch}) is a branch the publisher never force-pushes to. Use a branch of its own, such as gh-pages."
            : null;
    }

    /// <summary>What the remote says about its default branch and about the publishing branch.</summary>
    private sealed record RemoteState(string? DefaultBranch, string? Tip, string? Problem);

    private static async Task<RemoteState> ReadRemoteAsync(string git, string workDir, string remote, string branch, CancellationToken ct)
    {
        // The work folder may not exist yet; the command only needs some folder that does.
        var directory = Directory.Exists(workDir) ? workDir : Path.GetTempPath();
        var listing = await GitProcess.RunAsync(git, directory, ct, "ls-remote", "--symref", "--", remote, "HEAD", $"refs/heads/{branch}");
        if (!listing.Ok)
        {
            return new RemoteState(null, null, $"the remote could not be read: {listing.Message}");
        }

        string? defaultBranch = null;
        string? tip = null;
        foreach (var line in listing.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (DefaultBranchLine().Match(line) is { Success: true } head)
            {
                defaultBranch = head.Groups["branch"].Value;
            }
            else if (line.EndsWith($"\trefs/heads/{branch}", StringComparison.Ordinal))
            {
                tip = line.Split('\t')[0];
            }
        }

        return new RemoteState(defaultBranch, tip, null);
    }

    /// <summary>
    /// Null when the tip of the branch is a commit this publisher made. Otherwise what is there instead (the author), or why it
    /// could not be read: either way the branch is not overwritten.
    /// </summary>
    private static async Task<string?> ForeignTipAsync(string git, string workDir, string remote, string branch, CancellationToken ct)
    {
        var fetch = await GitProcess.RunAsync(git, workDir, ct, "fetch", "--quiet", "--depth=1", "--", remote, $"refs/heads/{branch}");
        if (!fetch.Ok)
        {
            return $"its tip could not be fetched: {fetch.Message}";
        }

        var author = await GitProcess.RunAsync(git, workDir, ct, "log", "-1", "--format=%ae", "FETCH_HEAD");
        if (!author.Ok)
        {
            return $"its tip could not be read: {author.Message}";
        }

        return author.Stdout == AuthorEmail ? null : "its last commit has another author";
    }

    /// <summary>Runs a git step; returns null on success, else the git message.</summary>
    private static async Task<string?> Git(string git, string workDir, CancellationToken ct, params string[] args)
    {
        var result = await GitProcess.RunAsync(git, workDir, ct, args);
        return result.Ok ? null : result.Message;
    }

    /// <summary>
    /// Makes the work folder empty: an existing publisher folder is replaced, an empty folder is used, any other folder
    /// with content is refused so the publisher never deletes files it did not create.
    /// </summary>
    private static bool TryPrepareWorkDir(string workDir, out string problem)
    {
        problem = string.Empty;
        if (Directory.Exists(workDir))
        {
            if (File.Exists(Path.Combine(workDir, ".git", MarkerName)))
            {
                DeleteTree(workDir);
            }
            else if (Directory.EnumerateFileSystemEntries(workDir).Any())
            {
                problem = "Arena:Site:WorkDir exists, has content and is not a publisher folder. Choose an empty folder.";
                return false;
            }
        }
        Directory.CreateDirectory(workDir);
        return true;
    }

    private static void DeleteTree(string path)
    {
        // Git writes its object files read-only, and Windows will not delete those.
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }
        Directory.Delete(path, recursive: true);
    }

    private static (int Files, long Bytes) CopyTree(string from, string to)
    {
        var files = 0;
        long bytes = 0;
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(file) == SiteFolders.ExportMarker)
            {
                continue;
            }

            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
            files++;
            bytes += new FileInfo(file).Length;
        }
        return (files, bytes);
    }

    private SitePublishResult Refused(string detail) => new(false, null, detail, time.GetUtcNow());

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._/-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex BranchName();

    [GeneratedRegex(@"^ref: refs/heads/(?<branch>\S+)\tHEAD$", RegexOptions.CultureInvariant)]
    private static partial Regex DefaultBranchLine();
}
