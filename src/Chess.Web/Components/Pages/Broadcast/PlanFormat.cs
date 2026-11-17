using System.Globalization;

namespace Chess.Web.Components.Pages.Broadcast;

/// <summary>Display helpers of the planner page. Every time is UTC: the list says so.</summary>
public static class PlanFormat
{
    public static string Utc(DateTimeOffset at) => at.ToUniversalTime().ToString("ddd HH:mm 'UTC'", CultureInfo.InvariantCulture);

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
