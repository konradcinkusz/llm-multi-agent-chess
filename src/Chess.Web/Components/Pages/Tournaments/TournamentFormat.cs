using System.Globalization;

namespace Chess.Web.Components.Pages.Tournaments;

/// <summary>Text and timing shared by the tournament pages.</summary>
public static class TournamentFormat
{
    /// <summary>How often the tournament pages reload while a tournament can still change.</summary>
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(5);

    public static bool IsRunning(string status) => status == "running";

    public static string Status(string status) => status switch
    {
        "running" => "Running",
        "finished" => "Finished",
        "cancelled" => "Cancelled",
        _ => status,
    };

    /// <summary>A premium-request figure; "unknown" when the model's multiplier is not known, as the API reports it.</summary>
    public static string Requests(double? value) =>
        value is { } requests ? requests.ToString("#,##0.#", CultureInfo.InvariantCulture) : "unknown";

    public static string Multiplier(double? value) =>
        value is { } multiplier ? "×" + multiplier.ToString("0.##", CultureInfo.InvariantCulture) : "multiplier unknown";

    public static string Fixed(double value, string format) => value.ToString(format, CultureInfo.InvariantCulture);

    public static string Percent(double value) => value.ToString("P0", CultureInfo.InvariantCulture);

    public static string When(DateTimeOffset at) =>
        at.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);
}
