using System.Reflection;
using Chess.Api.Catalog;
using Chess.Api.Contracts;
using Chess.Api.Players.Prompts;
using Chess.Api.Results;

namespace Chess.Api.Benchmarks;

/// <summary>
/// What a reader needs to compare two reports (03 §12.2): the app version, the rating formula version and the prompt version.
/// The prompt version is the version of the candidate's prompt style (03 §14.3); a hash of its texts, so a change to the wording
/// changes it without anyone having to remember to bump a number. A plain candidate id means classic.
/// </summary>
public static class BenchmarkFingerprint
{
    public static BenchmarkFingerprintDto Create(DateTimeOffset generatedAt, string? candidate = null) =>
        new(AppVersion(), RatingSnapshotWriter.FormulaVersion, PromptVersion(candidate), generatedAt);

    /// <summary>The informational version of this build, which carries the git commit when the build knew it.</summary>
    public static string AppVersion() =>
        typeof(BenchmarkFingerprint).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "unknown";

    /// <summary>The version of the prompt style a candidate id names (classic for a plain id).</summary>
    public static string PromptVersion(string? candidate = null)
    {
        var style = PromptStyles.Find(ModelVariant.StyleOf(candidate ?? string.Empty)) ?? PromptStyles.Classic;
        return PromptStyles.VersionOf(style);
    }
}
