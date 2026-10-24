using Microsoft.Playwright;

namespace Chess.E2E;

/// <summary>
/// The pages are prerendered by the server and start responding to input only once their Blazor circuit has connected.
/// An input that lands earlier is dropped without an error, so the journeys send it again until the page shows that it
/// took effect. Use only for inputs that are safe to repeat, and only with an effect the input alone must cause.
/// </summary>
/// <summary>
/// Completes once the page's Blazor circuit has answered its StartCircuit request. Until then the prerendered page
/// accepts input that the server then discards when it re-initialises the page. Create it before navigating, so the
/// websocket is seen from the start.
/// </summary>
public sealed class CircuitWatch
{
    private readonly TaskCompletionSource _connected = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public CircuitWatch(IPage page)
    {
        page.WebSocket += (_, socket) =>
        {
            var startSent = false;
            socket.FrameSent += (_, frame) =>
            {
                startSent |= frame.Text?.Contains("StartCircuit", StringComparison.Ordinal) == true;
            };
            socket.FrameReceived += (_, _) =>
            {
                if (startSent)
                {
                    _connected.TrySetResult();
                }
            };
        };
    }

    public Task WaitAsync() => _connected.Task.WaitAsync(TimeSpan.FromSeconds(30));
}

public static class Interaction
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(60);

    /// <param name="input">The user action, repeated until <paramref name="effect"/> holds.</param>
    /// <param name="effect">Web-first assertions with a short timeout; a failure means the input was not seen yet.</param>
    public static async Task RepeatUntilAsync(Func<Task> input, Func<Task> effect)
    {
        var deadline = DateTimeOffset.UtcNow + Limit;
        while (true)
        {
            await input();
            try
            {
                await effect();
                return;
            }
            catch (Exception ex) when (ex is PlaywrightException or TimeoutException && DateTimeOffset.UtcNow < deadline)
            {
                // Not seen yet. Once the deadline has passed the last failure is thrown as it is.
            }
        }
    }
}
