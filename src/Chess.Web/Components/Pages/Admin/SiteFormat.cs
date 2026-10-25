using System.Globalization;

namespace Chess.Web.Components.Pages.Admin;

/// <summary>Text for the site page. Pure, so the page's wording is tested without rendering it.</summary>
public static class SiteFormat
{
    public static string Size(long bytes) => bytes switch
    {
        < 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes} B"),
        < 1024 * 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024.0:0.0} KB"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024.0 * 1024.0):0.0} MB"),
    };

    public static string Stamp(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);

    public static string AutoPublish(int minutes) => minutes <= 0
        ? "Off. Set Arena:Site:AutoPublishMinutes to publish when a game has finished."
        : $"Every {minutes} minutes, when a game has finished since the last publish.";
}
