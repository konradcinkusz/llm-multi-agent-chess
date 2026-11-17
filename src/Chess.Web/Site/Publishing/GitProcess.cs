using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Chess.Web.Site.Publishing;

/// <summary>The outcome of one git command. ExitCode -1: git could not be started.</summary>
public sealed record GitResult(int ExitCode, string Stdout, string Stderr)
{
    public bool Ok => ExitCode == 0;

    public string Message => string.IsNullOrWhiteSpace(Stderr) ? Stdout : Stderr;
}

/// <summary>
/// Runs the git CLI with an argument list and no shell, so the same code runs on Windows and Linux. A credential prompt is
/// disabled: a publish that needs one fails instead of waiting for input.
/// </summary>
internal static partial class GitProcess
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

    private static readonly string[] RepositoryVariables =
        ["GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE", "GIT_OBJECT_DIRECTORY", "GIT_COMMON_DIR", "GIT_NAMESPACE", "GIT_PREFIX"];

    public static async Task<GitResult> RunAsync(string git, string workingDirectory, CancellationToken ct, params string[] args)
    {
        var start = new ProcessStartInfo(git)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        // The app may have been started from a git hook or a shell that exported these: they would point every command at
        // that repository instead of the folder it runs in.
        foreach (var name in RepositoryVariables)
        {
            start.Environment.Remove(name);
        }

        using var process = new Process { StartInfo = start };
        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return new GitResult(-1, string.Empty, $"git is not installed or not on the PATH ({ex.Message})");
        }

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            KillTree(process);
            if (ct.IsCancellationRequested)
            {
                throw;
            }
            return new GitResult(-1, string.Empty, $"git {args.FirstOrDefault()} did not finish within {Timeout.TotalMinutes:0} minutes");
        }

        return new GitResult(process.ExitCode, (await stdout).Trim(), ScrubUserInfo((await stderr).Trim()));
    }

    /// <summary>Git echoes the remote URL in its errors; a token in it (<c>https://user:token@host/…</c>) must not reach a page or a log.</summary>
    internal static string ScrubUserInfo(string text) => UserInfo().Replace(text, "$1");

    [GeneratedRegex(@"([a-z][a-z0-9+.-]*://)[^/@\s]+@", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UserInfo();

    private static void KillTree(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Already exited.
        }
    }
}
