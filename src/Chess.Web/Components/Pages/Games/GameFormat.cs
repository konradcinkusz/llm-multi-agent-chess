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
