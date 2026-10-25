using Chess.Web.Components.Pages.Play;
using Xunit;

namespace Chess.Web.Tests.Play;

public class ViewerPacerTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-10-25T10:00:00Z");

    [Fact]
    public void AMoveIsNotReleasedBeforeThePauseSinceTheLastOne()
    {
        var clock = new ManualClock(Start);
        var pacer = new ViewerPacer(clock) { Pause = TimeSpan.FromSeconds(2) };
        pacer.Reset(0);

        Assert.True(pacer.Advance(3));
        Assert.Equal(1, pacer.ShownPly);

        clock.Advance(TimeSpan.FromMilliseconds(1999));
        Assert.False(pacer.Advance(3));
        Assert.Equal(TimeSpan.FromMilliseconds(1), pacer.UntilNext(3));

        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(pacer.Advance(3));
        Assert.Equal(2, pacer.ShownPly);
    }

    [Fact]
    public void LiveReleasesEveryWaitingMoveAtOnce()
    {
        var clock = new ManualClock(Start);
        var pacer = new ViewerPacer(clock) { Pause = TimeSpan.Zero };
        pacer.Reset(0);

        Assert.True(pacer.Advance(4));
        Assert.Equal(4, pacer.ShownPly);
        Assert.Equal(TimeSpan.Zero, pacer.UntilNext(9));
        Assert.Equal(0, pacer.Behind(4));
    }

    [Fact]
    public void TheFirstMoveAfterAResetIsReleasedAtOnce()
    {
        var clock = new ManualClock(Start);
        var pacer = new ViewerPacer(clock) { Pause = TimeSpan.FromSeconds(5) };

        pacer.Reset(12);

        Assert.Equal(TimeSpan.Zero, pacer.UntilNext(13));
        Assert.True(pacer.Advance(13));
        Assert.Equal(13, pacer.ShownPly);
    }

    [Fact]
    public void BehindCountsTheMovesNotShownAndCatchUpShowsThemAllAtOnce()
    {
        var clock = new ManualClock(Start);
        var pacer = new ViewerPacer(clock) { Pause = TimeSpan.FromSeconds(3) };
        pacer.Reset(0);
        pacer.Advance(5);

        Assert.Equal(4, pacer.Behind(5));

        pacer.Reset(5);

        Assert.Equal(0, pacer.Behind(5));
        Assert.Equal(5, pacer.ShownPly);
    }

    [Fact]
    public void WhileBehindTheMovesComeOneEachPauseNotAllAtOnce()
    {
        var clock = new ManualClock(Start);
        var pacer = new ViewerPacer(clock) { Pause = TimeSpan.FromSeconds(1) };
        pacer.Reset(0);

        pacer.Advance(3);
        Assert.Equal(1, pacer.ShownPly);

        for (var second = 1; second <= 2; second++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            Assert.True(pacer.Advance(3));
            Assert.Equal(second + 1, pacer.ShownPly);
        }

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.False(pacer.Advance(3));
    }

    [Fact]
    public void ChangingToLiveReleasesTheMovesThatWaited()
    {
        var clock = new ManualClock(Start);
        var pacer = new ViewerPacer(clock) { Pause = TimeSpan.FromSeconds(5) };
        pacer.Reset(0);
        pacer.Advance(4);

        pacer.Pause = TimeSpan.Zero;

        Assert.True(pacer.Advance(4));
        Assert.Equal(4, pacer.ShownPly);
    }
}
