using Chess.Web.Components.Pages.Admin;
using Xunit;

namespace Chess.Web.Tests.Site.Publishing;

public class SiteFormatTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(2 * 1024 * 1024, "2.0 MB")]
    public void Sizes_read_in_the_largest_fitting_unit(long bytes, string expected) =>
        Assert.Equal(expected, SiteFormat.Size(bytes));

    [Fact]
    public void Stamps_are_utc_to_the_minute() =>
        Assert.Equal("2026-10-25 14:03 UTC", SiteFormat.Stamp(new DateTimeOffset(2026, 10, 25, 14, 3, 40, TimeSpan.Zero)));

    [Fact]
    public void Auto_publish_says_when_it_is_off_and_how_often_it_runs() =>
        Assert.Equal(new[] { "Off. Set Arena:Site:AutoPublishMinutes to publish when a game has finished.",
                             "Every 15 minutes, when a game has finished since the last publish." },
            new[] { SiteFormat.AutoPublish(0), SiteFormat.AutoPublish(15) });
}
