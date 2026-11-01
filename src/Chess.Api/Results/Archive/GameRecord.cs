using System.Text.Json;
using System.Text.Json.Serialization;

namespace Chess.Api.Results.Archive;

/// <summary>One side of an archived game. <see cref="Kind"/> is <c>llm</c> or <c>random</c>; a random side's model is <c>random</c>.</summary>
public sealed record RecordSide(string Kind, string Model);

/// <summary>
/// One move of an archived game, in SAN. Only what the rating and the cost figures read is kept: how many attempts the move
/// took, its latency, tokens and the move-quality figures. Prompts and raw replies stay in the owner's database.
/// </summary>
public sealed record RecordMove(
    string San,
    int Attempts = 1,
    long LatencyMs = 0,
    int? InputTokens = null,
    int? OutputTokens = null,
    int? CentipawnLoss = null,
    string? BestSan = null);

/// <summary>
/// One finished rated game as a file of the results archive (docs/RESULTS-ARCHIVE.md). The file is immutable: a game is added
/// once and never edited, so two pull requests never touch the same file. The ratings are not in it; they are computed from
/// the games (RATING.md), as for the owner's own games.
/// </summary>
public sealed record GameRecord(
    int Version,
    Guid Id,
    RecordSide White,
    RecordSide Black,
    string Result,
    string Termination,
    string? StartFen,
    DateTimeOffset PlayedAt,
    IReadOnlyList<RecordMove> Moves)
{
    public const int CurrentVersion = 1;
}

/// <summary>The one JSON shape of the archive: camelCase, indented, nulls left out, so a file diffs line by line.</summary>
public static class GameRecordJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static string Serialize(GameRecord record) => JsonSerializer.Serialize(record, Options) + "\n";

    public static GameRecord Deserialize(string json) =>
        JsonSerializer.Deserialize<GameRecord>(json, Options) ?? throw new JsonException("The file holds null.");

    /// <summary>The file of a game, relative to the archive folder: <c>games/yyyy/MM/&lt;id&gt;.json</c>, by the time it was played (UTC).</summary>
    public static string RelativePath(GameRecord record)
    {
        var at = record.PlayedAt.UtcDateTime;
        return $"games/{at:yyyy}/{at:MM}/{record.Id:D}.json";
    }
}
