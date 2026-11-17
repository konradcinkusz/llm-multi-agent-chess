namespace Chess.Analysis;

/// <summary>A UCI engine that can search one position at a fixed depth. Implemented by the process wrapper and by test doubles.</summary>
internal interface IUciEngine : IAsyncDisposable
{
    /// <summary>
    /// False while the engine has failed (it would not start or answer the handshake, died or hung in a search) and the
    /// retry delay has not passed. True before the first use: an engine that has not been tried is not known to be broken.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>Starts the engine if it is not running and completes the UCI handshake. Throws when it cannot.</summary>
    Task EnsureReadyAsync(CancellationToken ct);

    Task<UciSearch> SearchAsync(string fen, IReadOnlyList<string> uciMoves, int depth, CancellationToken ct);
}
