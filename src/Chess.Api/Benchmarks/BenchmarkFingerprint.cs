using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Chess.Api.Contracts;
using Chess.Api.Players;
using Chess.Api.Results;
using Chess.Engine;

namespace Chess.Api.Benchmarks;

/// <summary>
/// What a reader needs to compare two reports (03 §12.2): the app version, the rating formula version and the rated prompt
/// version. The prompt version is a hash of the prompt text (the system prompt and one move prompt), so a change to the
/// wording changes it without anyone having to remember to bump a number.
/// </summary>
public static class BenchmarkFingerprint
{
    public static BenchmarkFingerprintDto Create(DateTimeOffset generatedAt) =>
        new(AppVersion(), RatingSnapshotWriter.FormulaVersion, PromptVersion(), generatedAt);

    /// <summary>The informational version of this build, which carries the git commit when the build knew it.</summary>
    public static string AppVersion() =>
        typeof(BenchmarkFingerprint).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "unknown";

    public static string PromptVersion()
    {
        var sample = new MoveRequest(Guid.Empty, 0, ChessBoard.StartingPosition().ToFen(), ["e4", "d4"], []);
        var text = string.Join('\n', RatedPromptBuilder.SystemPrompt, RatedPromptBuilder.Build(sample),
            RatedPromptBuilder.Corrective("e5", ["e4", "d4"]));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return "sha256:" + Convert.ToHexString(hash)[..12].ToLowerInvariant();
    }
}
