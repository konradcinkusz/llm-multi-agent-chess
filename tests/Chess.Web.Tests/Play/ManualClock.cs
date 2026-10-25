namespace Chess.Web.Tests.Play;

/// <summary>A clock the test moves by hand, so the viewer pace is tested without waiting for real seconds.</summary>
internal sealed class ManualClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
