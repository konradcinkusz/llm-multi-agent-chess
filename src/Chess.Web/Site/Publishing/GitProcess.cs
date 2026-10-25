using System.ComponentModel;
using System.Diagnostics;

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
internal static class GitProcess
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

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

        return new GitResult(process.ExitCode, (await stdout).Trim(), (await stderr).Trim());
    }

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
