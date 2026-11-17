using Chess.Web.Api;
using Chess.Web.Site.Publishing;
using Chess.Web.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Chess.Web.Tests.Site.Publishing;

/// <summary>Auto-publish runs only when the number of finished games changed since the last publish (fake clock, no git, no network).</summary>
public class SiteAutoPublisherTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 10, 25, 10, 0, 0, TimeSpan.Zero);

    // Each test exports into a folder of its own, never the default one in the system temp directory.
    private readonly SiteTestGit _git = new();

    public void Dispose() => _git.Dispose();

    [Fact]
    public async Task Nothing_happens_when_the_interval_is_zero_even_with_new_games()
    {
        var (auto, exporter, publisher, activity) = Build(minutes: 0, startedWith: 5);
        activity.Finished = 9;

        var result = await auto.TickAsync(default);

        Assert.Null(result);
        Assert.Equal(0, exporter.Calls);
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task Nothing_is_published_when_no_game_finished_since_the_app_started()
    {
        var (auto, exporter, publisher, _) = Build(minutes: 15, startedWith: 30);

        var result = await auto.TickAsync(default);

        Assert.Null(result);
        Assert.Equal(0, exporter.Calls);
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task A_game_finished_after_the_start_is_published_once_then_not_again_until_the_next_game()
    {
        var (auto, exporter, publisher, activity) = Build(minutes: 15, startedWith: 30);
        activity.Finished = 31;

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
        var (auto, exporter, publisher, activity) = Build(minutes: 15, startedWith: 30);
        activity.Finished = 31;
        await auto.TickAsync(default);

        activity.Finished = 32;
        var result = await auto.TickAsync(default);

        Assert.True(result is { Published: true });
        Assert.Equal(2, exporter.Calls);
        Assert.Equal(2, publisher.Published.Count);
    }

    [Fact]
    public async Task A_deleted_game_changes_the_site_too()
    {
        var (auto, exporter, _, activity) = Build(minutes: 15, startedWith: 30);
        activity.Finished = 29;

        var result = await auto.TickAsync(default);

        Assert.True(result is { Published: true });
        Assert.Equal(1, exporter.Calls);
    }

    [Fact]
    public async Task A_failed_publish_is_not_counted_so_the_next_tick_tries_again()
    {
        var (auto, exporter, _, activity) = Build(minutes: 15, published: false, startedWith: 30);
        activity.Finished = 31;

        var first = await auto.TickAsync(default);
        var second = await auto.TickAsync(default);

        Assert.True(first is { Published: false });
        Assert.True(second is { Published: false });
        Assert.Equal(2, exporter.Calls);
    }

    [Fact]
    public async Task When_the_api_is_not_up_at_the_start_the_first_tick_that_can_read_takes_the_count_and_publishes_nothing()
    {
        var (auto, exporter, _, activity) = Build(minutes: 15, startedWith: 0, apiUp: false);
        activity.Down = false;
        activity.Finished = 40;

        var first = await auto.TickAsync(default);
        activity.Finished = 41;
        var second = await auto.TickAsync(default);

        Assert.Null(first);
        Assert.True(second is { Published: true });
        Assert.Equal(1, exporter.Calls);
    }

    [Fact]
    public async Task A_game_that_finishes_inside_an_older_run_is_seen_because_the_total_changes()
    {
        // The games of one run share a creation time, so the 20 newest by creation time never include a later finish in an
        // older run. The total of finished games does change; the activity reads exactly that.
        var total = 100;
        var handler = new FakeHandler(request => Canned.Json(
            $$"""{"items":[],"page":1,"pageSize":1,"total":{{total}}}"""));
        var api = new ArenaApiClient(Canned.Client(handler));
        var activity = new ArenaSiteActivity(api);

        var before = await activity.FinishedGamesAsync(default);
        total = 101;
        var after = await activity.FinishedGamesAsync(default);

        Assert.Equal((100, 101), (before, after));
        Assert.All(handler.Requests, request => Assert.Contains("status=finished", request.Target, StringComparison.Ordinal));
    }

    private (SiteAutoPublisher Auto, SiteFakeExporter Exporter, SiteFakePublisher Publisher, SiteFakeActivity Activity)
        Build(int minutes, bool published = true, int startedWith = 0, bool apiUp = true)
    {
        var clock = new SiteFakeClock(Start);
        var exporter = new SiteFakeExporter { Clock = clock };
        var publisher = new SiteFakePublisher(published) { At = Start };
        var activity = new SiteFakeActivity { Finished = startedWith, Down = !apiUp };
        var options = SiteTestOptions.Create(o =>
        {
            o.AutoPublishMinutes = minutes;
            o.ExportDirectory = Path.Combine(_git.Root, "export");
            o.WorkDir = _git.WorkDir;
        });
        var app = Directory.CreateDirectory(Path.Combine(_git.Root, "app")).FullName;
        var coordinator = new SiteExportCoordinator(publisher, options, new SiteFakeHostEnvironment(app), clock, exporter);
        var auto = new SiteAutoPublisher(options, coordinator, activity, clock, NullLogger<SiteAutoPublisher>.Instance);
        auto.ReadBaselineAsync(default).GetAwaiter().GetResult();
        return (auto, exporter, publisher, activity);
    }
}
