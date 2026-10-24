using System.Globalization;

namespace Chess.Web.Components.Pages.Broadcast;

/// <summary>Display and input helpers of the planner page. Every time is UTC: the form and the timeline say so.</summary>
public static class PlanFormat
{
    /// <summary>The value format of an <c>&lt;input type="datetime-local"&gt;</c>, read as UTC.</summary>
    public const string StartInputFormat = "yyyy-MM-ddTHH:mm";

    public static string Utc(DateTimeOffset at) => at.ToUniversalTime().ToString("ddd HH:mm 'UTC'", CultureInfo.InvariantCulture);

    public static string Time(DateTimeOffset at) => at.ToUniversalTime().ToString("HH:mm", CultureInfo.InvariantCulture);

    public static string ToStartInput(DateTimeOffset at) => at.ToUniversalTime().ToString(StartInputFormat, CultureInfo.InvariantCulture);

    public static DateTimeOffset? ParseStartInput(string? text) =>
        DateTimeOffset.TryParseExact(text, StartInputFormat, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var at)
            ? at
            : null;

    /// <summary>The next whole hour after <paramref name="now"/>, in UTC: the default start of a plan.</summary>
    public static DateTimeOffset NextFullHour(DateTimeOffset now)
    {
        var utc = now.UtcDateTime;
        return new DateTimeOffset(utc.Date.AddHours(utc.Hour + 1), TimeSpan.Zero);
    }

    public static string Status(string status) => status switch
    {
        ScheduleStatuses.Draft => "Draft",
        ScheduleStatuses.Scheduled => "Scheduled",
        ScheduleStatuses.Running => "Running",
        ScheduleStatuses.Paused => "Paused",
        ScheduleStatuses.Finished => "Finished",
        ScheduleStatuses.Cancelled => "Cancelled",
        _ => status,
    };
}
