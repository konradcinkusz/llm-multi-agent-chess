using System.Globalization;
using Chess.Web.Api;

namespace Chess.Web.Components.Pages.Games;

/// <summary>Text for the game pages. The values 03 §5.1 defines are spelled out; anything else is shown as sent.</summary>
public static class GameFormat
{
    public static string Matchup(GameSummaryDto game) => $"{game.White.ModelId} vs {game.Black.ModelId}";

    public static string State(string state) => state switch
    {
        "queued" => "Queued",
        "running" => "Running",
        "finished" => "Finished",
        _ => state,
    };

    /// <summary>"insufficient-material" reads as "insufficient material"; a game without a termination yet shows a dash.</summary>
    public static string Termination(string? termination) => termination?.Replace('-', ' ') ?? "—";

    public static string Side(string color) => color == "white" ? "White" : "Black";

    public static string When(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);

    /// <summary>The list's date column: the same moment as <see cref="When"/> without the zone, which the column header gives.</summary>
    public static string Compact(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    /// <summary>What a PGN result says in words. A game still in play has no result, so it is not decided.</summary>
    public static string ResultMeaning(string result) => result switch
    {
        "1-0" => "White won",
        "0-1" => "Black won",
        "1/2-1/2" => "Draw",
        _ => "Not decided",
    };

    /// <summary>A decided game is a success badge; a draw or an open game is neutral.</summary>
    public static string ResultTone(string result) => result is "1-0" or "0-1" ? "badge-success" : "badge-neutral";

    public static string Latency(long milliseconds) => milliseconds < 1000
        ? $"{milliseconds.ToString(CultureInfo.InvariantCulture)} ms"
        : $"{(milliseconds / 1000.0).ToString("0.0", CultureInfo.InvariantCulture)} s";

    public static string Tokens(int? input, int? output) => input is null && output is null
        ? "not reported"
        : $"{Count(input)} in / {Count(output)} out";

    /// <summary>Null when Stockfish did not analyse the move (03 §3.5).</summary>
    public static string CentipawnLoss(int? loss) => loss is null
        ? "not analysed"
        : $"{loss.Value.ToString(CultureInfo.InvariantCulture)} cp";

    private static string Count(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "—";
}
