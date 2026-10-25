using Chess.Web.Api;

namespace Chess.Web.Components.Shared.Game;

/// <summary>
/// One attempt at a move, as the engine log shows it (03 §10.4). Built from <c>GET /api/attempts</c> (<see cref="From"/>)
/// or from an <c>AttemptRecorded</c> event. The side is the colour of the ply: ply 1 is White's.
/// </summary>
public sealed record EngineEntry(int Ply, int Attempt, string ModelId, string Prompt, string? RawReply, string? Reasoning,
    string? ParsedSan, bool Legal, string? RejectionKind, long LatencyMs, int? InputTokens, int? OutputTokens,
    int? ReasoningTokens, double? AiCredits, DateTimeOffset? At)
{
    /// <summary>The default <c>Arena:MaxAttemptsPerMove</c>: a side that makes this many illegal replies forfeits (03 §10.3).</summary>
    public const int MaxAttempts = 3;

    public string Color => Ply % 2 == 1 ? "White" : "Black";

    public static EngineEntry From(AttemptDto attempt) => new(attempt.Ply, attempt.Number, attempt.ModelId, attempt.Prompt,
        attempt.RawReply, attempt.Reasoning, attempt.ParsedSan, attempt.Legal, attempt.RejectionKind, attempt.LatencyMs,
        attempt.InputTokens, attempt.OutputTokens, attempt.ReasoningTokens, attempt.AiCredits, attempt.CreatedAt);
}

/// <summary>The verdict badge of an attempt: the kinds of 03 §10.3 in words, and the tone of the badge (07 §4).</summary>
public static class EngineVerdict
{
    public static string Label(EngineEntry entry) => entry.Legal ? "legal" : entry.RejectionKind ?? "illegal";

    public static string Tone(EngineEntry entry) => entry.Legal ? "badge-success" : entry.RejectionKind switch
    {
        // A reply that could be a move, or that names more than one, is a warning; the rest are failures.
        "unparseable" or "ambiguous" => "badge-warning",
        _ => "badge-danger",
    };
}
