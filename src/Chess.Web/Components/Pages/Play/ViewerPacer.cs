namespace Chess.Web.Components.Pages.Play;

/// <summary>
/// How far the live page has shown the game (03 §10.5). The server plays at full speed; the page releases one move per pause,
/// never faster. A zero pause is "Live": every move is released at once. Display only: nothing here reaches the server.
/// Pure apart from the clock, so the rule is tested with a manual <see cref="TimeProvider"/>.
/// </summary>
public sealed class ViewerPacer(TimeProvider clock)
{
    private DateTimeOffset? _lastRelease;

    /// <summary>The minimum time between two moves as shown. Zero is Live.</summary>
    public TimeSpan Pause { get; set; }

    /// <summary>The last ply shown. Zero is the start position.</summary>
    public int ShownPly { get; private set; }

    /// <summary>The moves the game has played that the page does not show yet.</summary>
    public int Behind(int appliedPly) => Math.Max(0, appliedPly - ShownPly);

    /// <summary>
    /// Shows the position at <paramref name="ply"/> at once: a page opened on a game, the start of a game, or "Catch up".
    /// The next move is not held back, because no move was shown before it.
    /// </summary>
    public void Reset(int ply)
    {
        ShownPly = ply;
        _lastRelease = null;
    }

    /// <summary>How long the next move waits for the pause, or zero when it can be released now.</summary>
    public TimeSpan UntilNext(int appliedPly)
    {
        if (ShownPly >= appliedPly || Pause == TimeSpan.Zero || _lastRelease is not { } last)
        {
            return TimeSpan.Zero;
        }

        var remaining = last + Pause - clock.GetUtcNow();
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    /// <summary>
    /// Releases the next move when the pause since the last one has passed; Live releases every move the game has.
    /// Returns true when the shown ply changed.
    /// </summary>
    public bool Advance(int appliedPly)
    {
        if (ShownPly >= appliedPly || UntilNext(appliedPly) > TimeSpan.Zero)
        {
            return false;
        }

        ShownPly = Pause == TimeSpan.Zero ? appliedPly : ShownPly + 1;
        _lastRelease = clock.GetUtcNow();
        return true;
    }
}
