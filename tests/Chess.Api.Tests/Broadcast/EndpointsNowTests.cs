using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Chess.Api.Broadcast;
using Chess.Api.Contracts;
using Xunit;

namespace Chess.Api.Tests.Broadcast;

/// <summary>
/// <c>GET /api/broadcast/now</c> (03 §9.2, §9.4): the answer of <see cref="IBroadcastController.NowAsync"/>, passed
/// through unchanged to the broadcast page and OBS. The controller decides what is showing; these tests pin the shapes.
/// </summary>
public class EndpointsNowTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 25, 0, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly PlayerSpec FakeA = new("llm", "fake-a");
    private static readonly PlayerSpec FakeB = new("llm", "fake-b");
    private static readonly TimeSpan SlotLength = TimeSpan.FromMinutes(10);

    [Fact]
    public async Task Now_is_idle_with_nothing_on_screen_when_no_schedule_is_playing()
    {
        using var h = new BroadcastHarness();
        h.App.Controller.Now = new NowNextDto("idle", null, [], Notice: null, EndsAt: null);

        var now = await NowAsync(h);

        Assert.Equal("idle", now.Status);
        Assert.Null(now.Current);
        Assert.Empty(now.Next);
        Assert.Null(now.Notice);
        Assert.Null(now.EndsAt);
    }

    [Fact]
    public async Task Now_while_running_carries_the_game_in_play_the_next_slots_and_the_window_end()
    {
        using var h = new BroadcastHarness();
        var playing = Slot(1, SlotState.Running);
        var next = new[] { Slot(2, SlotState.Pending), Slot(3, SlotState.Pending), Slot(4, SlotState.Pending) };
        h.App.Controller.Now = new NowNextDto("running", playing, next, Notice: null, EndsAt: Start.AddHours(24));

        var now = await NowAsync(h);

        Assert.Equal("running", now.Status);
        Assert.Equal(playing, now.Current);
        Assert.Equal(next, now.Next);
        Assert.Equal(Start.AddHours(24), now.EndsAt);
        Assert.Null(now.Notice);
    }

    [Fact]
    public async Task Now_between_games_has_no_current_slot()
    {
        using var h = new BroadcastHarness();
        h.App.Controller.Now = new NowNextDto("running", null, [Slot(2, SlotState.Pending)], Notice: null,
            EndsAt: Start.AddHours(24));

        var now = await NowAsync(h);

        Assert.Equal("running", now.Status);
        Assert.Null(now.Current);
        Assert.Equal(new[] { 2 }, now.Next.Select(s => s.Index));
    }

    [Fact]
    public async Task Now_while_paused_reports_paused_with_the_game_it_holds()
    {
        using var h = new BroadcastHarness();
        h.App.Controller.Now = new NowNextDto("paused", Slot(1, SlotState.Running), [Slot(2, SlotState.Pending)],
            Notice: null, EndsAt: Start.AddHours(24));

        var now = await NowAsync(h);

        Assert.Equal("paused", now.Status);
        Assert.Equal(1, now.Current?.Index);
    }

    [Fact]
    public async Task Now_carries_the_technical_break_notice_when_the_controller_is_backing_off()
    {
        using var h = new BroadcastHarness();
        const string notice = "Technical break: retrying game 17 in 30 s";
        h.App.Controller.Now = new NowNextDto("running", null, [Slot(1, SlotState.Pending)], notice,
            Start.AddHours(24));

        var now = await NowAsync(h);

        Assert.Equal(notice, now.Notice);
        Assert.Null(now.Current);
    }

    private static SlotDto Slot(int index, string state)
    {
        var start = Start.AddMinutes(index * (SlotLength.TotalMinutes + 10));
        return new SlotDto(Guid.NewGuid(), index, FakeA, FakeB, start, start + SlotLength, state, null);
    }

    private static async Task<NowNextDto> NowAsync(BroadcastHarness h)
    {
        var response = await h.Client.GetAsync("/api/broadcast/now");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<NowNextDto>(Json))!;
    }
}
