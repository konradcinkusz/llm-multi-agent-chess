using System.Diagnostics;
using Chess.Web.Site;
using Chess.Web.Site.Publishing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.FileProviders;

namespace Chess.Web.Tests.Site.Publishing;

/// <summary>A temporary folder with a bare repository as the remote, and the git CLI run against it.</summary>
public sealed class SiteTestGit : IDisposable
{
    public string Root { get; } = Directory.CreateTempSubdirectory("arena-site-").FullName;

    public string Remote => Path.Combine(Root, "remote.git");

    public string Source => Path.Combine(Root, "source");

    public string WorkDir => Path.Combine(Root, "work");

    public void CreateBareRemote() => Run(Root, "init", "--bare", "--quiet", Remote);

    public static string Run(string directory, params string[] args)
    {
        var result = Start(directory, args);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', args)} failed: {result.Stderr}");
        }
        return result.Stdout;
    }

    public static (int ExitCode, string Stdout, string Stderr) Start(string directory, params string[] args)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdout.Trim(), stderr.Trim());
    }

    /// <summary>A repository with one commit on main, origin pointing at the bare remote.</summary>
    public void CreateSourceRepository()
    {
        Directory.CreateDirectory(Source);
        Run(Source, "init", "--quiet");
        Run(Source, "symbolic-ref", "HEAD", "refs/heads/main");
        File.WriteAllText(Path.Combine(Source, "README.md"), "owner's work\n");
        Run(Source, "add", "--all");
        Run(Source, "-c", "user.name=owner", "-c", "user.email=owner@example.invalid", "commit", "--quiet", "-m", "Initial");
        Run(Source, "remote", "add", "origin", Remote);
    }

    public void Dispose()
    {
        if (!Directory.Exists(Root))
        {
            return;
        }
        // Git writes read-only object files; Windows will not delete them otherwise.
        foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }
        Directory.Delete(Root, recursive: true);
    }

    /// <summary>A small export: a home page, one nested page and a JSON file.</summary>
    public static string WriteSite(string directory, string title = "Results")
    {
        Directory.CreateDirectory(Path.Combine(directory, "about"));
        Directory.CreateDirectory(Path.Combine(directory, "data"));
        File.WriteAllText(Path.Combine(directory, "index.html"), $"<!doctype html><h1>{title}</h1>");
        File.WriteAllText(Path.Combine(directory, "about", "index.html"), "<h1>About</h1>");
        File.WriteAllText(Path.Combine(directory, "data", "leaderboard.json"), "[]");
        return directory;
    }
}

public sealed class SiteFakeClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}

public sealed class SiteFakeHostEnvironment(string contentRoot) : IHostEnvironment
{
    public string EnvironmentName { get; set; } = "Test";
    public string ApplicationName { get; set; } = "Chess.Web.Tests";
    public string ContentRootPath { get; set; } = contentRoot;
    public IFileProvider ContentRootFileProvider { get; set; } = null!;
}

public sealed class SiteFakeExporter : ISiteExporter
{
    public int Calls { get; private set; }

    public Func<string, SiteExportResult>? Behaviour { get; set; }

    public DateTimeOffset TakenAt { get; set; }

    /// <summary>When set, each export is taken at the clock's time, as a real export is.</summary>
    public SiteFakeClock? Clock { get; set; }

    public Task<SiteExportResult> ExportAsync(string outputDirectory, CancellationToken ct)
    {
        Calls++;
        if (Behaviour is not null)
        {
            return Task.FromResult(Behaviour(outputDirectory));
        }
        SiteTestGit.WriteSite(outputDirectory);
        return Task.FromResult(new SiteExportResult(3, 1, 120, Clock?.GetUtcNow() ?? TakenAt));
    }
}

public sealed class SiteFakePublisher(bool published = true) : ISitePublisher
{
    public List<string> Published { get; } = [];

    public DateTimeOffset At { get; set; }

    public Task<SitePublishResult> PublishAsync(string siteDirectory, CancellationToken ct)
    {
        Published.Add(siteDirectory);
        return Task.FromResult(new SitePublishResult(published, published ? "abc1234" : null,
            published ? "Published 3 files" : "Refused by the fake", At));
    }
}

public sealed class SiteFakeActivity : ISiteActivity
{
    public DateTimeOffset? Latest { get; set; }

    public Task<DateTimeOffset?> LatestFinishedAsync(CancellationToken ct) => Task.FromResult(Latest);
}

public static class SiteTestOptions
{
    public static IOptions<SitePublishingOptions> Create(Action<SitePublishingOptions> configure)
    {
        var options = new SitePublishingOptions();
        configure(options);
        return Options.Create(options);
    }
}
