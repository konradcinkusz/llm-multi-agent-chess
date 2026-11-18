using Microsoft.Extensions.Logging;

namespace Chess.Llm;

/// <summary>
/// Removes the on-disk state of a Copilot session once its conversation is over. Disposing an SDK session only releases it in
/// memory, so a run that opens a session for every move would leave them all behind. Best effort: a failure is logged and never
/// reaches the game.
/// </summary>
internal static class SessionCleanup
{
    internal static readonly TimeSpan DeleteTimeout = TimeSpan.FromSeconds(5);

    public static async Task DeleteQuietlyAsync(Func<string, CancellationToken, Task> delete, string sessionId, ILogger logger,
        TimeSpan? timeout = null)
    {
        try
        {
            using var cancel = new CancellationTokenSource(timeout ?? DeleteTimeout);
            await delete(sessionId, cancel.Token).WaitAsync(cancel.Token);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            logger.LogWarning(ex, "Could not delete Copilot session {SessionId}; its state stays on disk", sessionId);
        }
    }
}
