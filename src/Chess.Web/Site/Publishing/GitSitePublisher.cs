using Microsoft.Extensions.Options;

namespace Chess.Web.Site.Publishing;

/// <summary>
/// 03 §11 and ADR-013: replaces the content of the publishing branch with one orphan commit of the export, force-pushed.
/// It works in its own folder (Arena:Site:WorkDir), never in the owner's working tree, so `main` and the working tree stay
/// untouched. Every expected failure is reported in the result with a detail; nothing is thrown for it.
/// </summary>
public sealed class GitSitePublisher(IOptions<SitePublishingOptions> options, TimeProvider time, string sourceDirectory) : ISitePublisher
{
    private const string MarkerName = "arena-site-publisher";
    private const string Identity = "-c user.name=llm-chess-arena -c user.email=site@llm-chess-arena.invalid -c commit.gpgSign=false";

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

        var (remote, problem) = await GitRemote.ResolveAsync(opts, sourceDirectory, ct);
        if (remote is null)
        {
            return Refused(problem!);
        }

        var workDir = opts.ResolvedWorkDir;
        if (IsInsideOrAbove(workDir, Path.GetFullPath(sourceDirectory)))
        {
            return Refused("Arena:Site:WorkDir must be a folder of its own, not the repository the app runs from.");
        }
        if (!TryPrepareWorkDir(workDir, out var prepareProblem))
        {
            return Refused(prepareProblem);
        }

        var git = opts.GitExecutable;
        var step = await Git(git, workDir, ct, "init", "--quiet");
        if (step is not null) return Refused($"git init failed: {step}");
        step = await Git(git, workDir, ct, "symbolic-ref", "HEAD", $"refs/heads/{opts.Branch}");
        if (step is not null) return Refused($"git could not name the branch {opts.Branch}: {step}");
        await File.WriteAllTextAsync(Path.Combine(workDir, ".git", MarkerName), "publisher folder\n", ct);

        var (files, bytes) = CopyTree(siteDirectory, workDir);
        await File.WriteAllTextAsync(Path.Combine(workDir, ".nojekyll"), string.Empty, ct);

        step = await Git(git, workDir, ct, "add", "--all");
        if (step is not null) return Refused($"git add failed: {step}");
        var at = time.GetUtcNow();
        step = await Git(git, workDir, ct, [.. Identity.Split(' '), "commit", "--quiet", "-m", $"Publish results site {at:yyyy-MM-dd HH:mm} UTC"]);
        if (step is not null) return Refused($"git commit failed: {step}");

        var head = await GitProcess.RunAsync(git, workDir, ct, "rev-parse", "HEAD");
        if (!head.Ok) return Refused($"git rev-parse failed: {head.Message}");
        var commit = head.Stdout;

        step = await Git(git, workDir, ct, "push", "--force", "--quiet", remote, $"HEAD:refs/heads/{opts.Branch}");
        if (step is not null) return Refused($"The push to {GitRemote.Redact(remote)} was refused: {step}");

        return new SitePublishResult(true, commit,
            $"Published {files} files ({bytes:N0} bytes) to {opts.Branch} of {GitRemote.Redact(remote)} as commit {commit[..7]}.", at);
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

    /// <summary>True when the two paths are the same, or one is inside the other.</summary>
    private static bool IsInsideOrAbove(string a, string b)
    {
        var left = a.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var right = b.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return left.Equals(right, comparison)
            || right.StartsWith(left + Path.DirectorySeparatorChar, comparison)
            || left.StartsWith(right + Path.DirectorySeparatorChar, comparison);
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
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
            files++;
            bytes += new FileInfo(file).Length;
        }
        return (files, bytes);
    }

    private SitePublishResult Refused(string detail) => new(false, null, detail, time.GetUtcNow());
}
