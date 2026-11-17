namespace Chess.Analysis;

/// <summary>
/// How long the Stockfish process may take, so that a hung or non-UCI binary cannot hold the analysis forever.
/// </summary>
/// <param name="Handshake">Start-up: the time to answer <c>uci</c> with <c>uciok</c> and <c>isready</c> with <c>readyok</c>.</param>
/// <param name="Search">One fixed-depth search, from <c>go</c> to <c>bestmove</c>. The process is killed when it is exceeded.</param>
/// <param name="RetryAfter">After a failure the engine counts as unavailable for this long, then the next call tries a fresh process.</param>
public sealed record UciTimeouts(TimeSpan Handshake, TimeSpan Search, TimeSpan RetryAfter)
{
    public static UciTimeouts Default { get; } = new(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(30));

    internal UciTimeouts Validated()
    {
        if (Handshake <= TimeSpan.Zero || Search <= TimeSpan.Zero || RetryAfter < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(UciTimeouts), "Handshake and search timeouts must be positive.");
        }

        return this;
    }
}
