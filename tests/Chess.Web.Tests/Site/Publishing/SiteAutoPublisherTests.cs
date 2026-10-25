using Chess.Web.Site.Publishing;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Chess.Web.Tests.Site.Publishing;

/// <summary>Auto-publish runs only when a game finished since the last publish (fake clock, no git, no network).</summary>
public class SiteAutoPublisherTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 25, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Nothing_happens_when_the_interval_is_zero_even_with_new_games()
    {
        var (auto, exporter, publisher, activity, clock) = Build(minutes: 0);
        activity.Latest = Start.AddMinutes(5);
        clock.Now = Start.AddMinutes(10);

        var result = await auto.TickAsync(default);

        Assert.Null(result);
        Assert.Equal(0, exporter.Calls);
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task Nothing_is_published_when_no_game_finished_since_the_app_started()
    {
        var (auto, exporter, publisher, activity, clock) = Build(minutes: 15);
        activity.Latest = Start.AddMinutes(-30);
        clock.Now = Start.AddMinutes(15);

        var result = await auto.TickAsync(default);

        Assert.Null(result);
        Assert.Equal(0, exporter.Calls);
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task A_game_finished_after_the_start_is_published_once_then_not_again_until_the_next_game()
    {
        var (auto, exporter, publisher, activity, clock) = Build(minutes: 15);
        activity.Latest = Start.AddMinutes(5);
        clock.Now = Start.AddMinutes(15);

        var first = await auto.TickAsync(default);
        var second = await auto.TickAsync(default);

        Assert.True(first is { Published: true });
        Assert.Null(second);
        Assert.Equal(1, exporter.Calls);
        Assert.Single(publisher.Published);
    }

    [Fact]
    public async Task A_game_finished_after_the_last_publish_is_published_again()
    {
        var (auto, exporter, publisher, activity, clock) = Build(minutes: 15);
        activity.Latest = Start.AddMinutes(5);
        clock.Now = Start.AddMinutes(15);
        await auto.TickAsync(default);

        activity.Latest = Start.AddMinutes(20);
        clock.Now = Start.AddMinutes(30);
        var result = await auto.TickAsync(default);

        Assert.True(result is { Published: true });
        Assert.Equal(2, exporter.Calls);
        Assert.Equal(2, publisher.Published.Count);
    }

    [Fact]
    public async Task A_failed_publish_is_not_counted_so_the_next_tick_tries_again()
    {
        var (auto, exporter, publisher, activity, clock) = Build(minutes: 15, published: false);
        activity.Latest = Start.AddMinutes(5);
        clock.Now = Start.AddMinutes(15);

        var first = await auto.TickAsync(default);
        var second = await auto.TickAsync(default);

        Assert.True(first is { Published: false });
        Assert.True(second is { Published: false });
        Assert.Equal(2, exporter.Calls);
    }

    private static (SiteAutoPublisher Auto, SiteFakeExporter Exporter, SiteFakePublisher Publisher, SiteFakeActivity Activity, SiteFakeClock Clock)
        Build(int minutes, bool published = true)
    {
        var clock = new SiteFakeClock(Start);
        var exporter = new SiteFakeExporter { Clock = clock };
        var publisher = new SiteFakePublisher(published) { At = Start };
        var activity = new SiteFakeActivity();
        var options = SiteTestOptions.Create(o => o.AutoPublishMinutes = minutes);
        var coordinator = new SiteExportCoordinator(publisher, options, new SiteFakeHostEnvironment(Path.GetTempPath()), clock, exporter);
        var auto = new SiteAutoPublisher(options, coordinator, activity, clock, NullLogger<SiteAutoPublisher>.Instance);
        return (auto, exporter, publisher, activity, clock);
    }
}
