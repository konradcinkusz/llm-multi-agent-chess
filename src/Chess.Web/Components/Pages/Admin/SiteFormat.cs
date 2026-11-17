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

    /// <summary>The size limit of a published GitHub Pages site: 1 GB.</summary>
    public const long PagesLimitBytes = 1024L * 1024 * 1024;

    /// <summary>From this share of the limit on, the page warns that the site is getting close to it.</summary>
    public const double PagesWarnShare = 0.8;

    /// <summary>How much of the Pages limit the export takes, in words.</summary>
    public static string PagesShare(long bytes) =>
        string.Create(CultureInfo.InvariantCulture, $"{100.0 * bytes / PagesLimitBytes:0.0}% of the 1 GB limit of a GitHub Pages site");

    public static bool NearPagesLimit(long bytes) => bytes >= PagesWarnShare * PagesLimitBytes;

    public static string Stamp(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);

    public static string AutoPublish(int minutes) => minutes <= 0
        ? "Off. Set Arena:Site:AutoPublishMinutes to publish when a game has finished."
        : $"Every {minutes} minutes, when a game has finished since the last publish.";
}
