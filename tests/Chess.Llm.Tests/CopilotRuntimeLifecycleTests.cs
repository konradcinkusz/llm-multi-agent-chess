using Chess.Llm;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Chess.Llm.Tests;

/// <summary>The Copilot runtime starts in the background, and a session is deleted once its conversation is over.</summary>
public class CopilotRuntimeLifecycleTests
{
    private sealed class FakeRuntime(Func<CancellationToken, Task> start) : ICopilotRuntime
    {
        public int Stops { get; private set; }

        public Task StartAsync(CancellationToken ct) => start(ct);

        public Task StopAsync()
        {
            Stops++;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task The_host_is_not_held_up_by_a_runtime_that_hangs_and_stopping_cancels_the_start()
    {
        var started = new TaskCompletionSource();
        CancellationToken seen = default;
        var runtime = new FakeRuntime(async ct =>
        {
            seen = ct;
            started.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
        });
        var service = new CopilotClientHostedService(runtime, NullLogger<CopilotClientHostedService>.Instance);

        // StartAsync returns although the runtime never finishes starting.
        await service.StartAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(seen.IsCancellationRequested);

        await service.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(seen.IsCancellationRequested);
        Assert.Equal(1, runtime.Stops);
    }

    [Fact]
    public async Task A_runtime_that_throws_while_starting_never_fails_the_host()
    {
        var runtime = new FakeRuntime(_ => throw new InvalidOperationException("boom"));
        var service = new CopilotClientHostedService(runtime, NullLogger<CopilotClientHostedService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(1, runtime.Stops);
    }

    [Fact]
    public async Task Stopping_twice_stops_the_runtime_once_and_does_not_throw()
    {
        var runtime = new FakeRuntime(_ => Task.CompletedTask);
        var service = new CopilotClientHostedService(runtime, NullLogger<CopilotClientHostedService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(1, runtime.Stops);
    }

    [Fact]
    public async Task A_session_is_deleted_by_its_id()
    {
        var deleted = new List<string>();

        await SessionCleanup.DeleteQuietlyAsync((id, _) =>
        {
            deleted.Add(id);
            return Task.CompletedTask;
        }, "session-1", NullLogger.Instance);

        Assert.Equal(["session-1"], deleted);
    }

    [Fact]
    public async Task A_failed_delete_never_reaches_the_game()
    {
        await SessionCleanup.DeleteQuietlyAsync((_, _) => throw new InvalidOperationException("Session does not exist."),
            "session-1", NullLogger.Instance);
        await SessionCleanup.DeleteQuietlyAsync((_, _) => Task.FromException(new IOException("disk")),
            "session-2", NullLogger.Instance);
    }

    [Fact]
    public async Task A_delete_that_hangs_is_given_up_after_its_timeout()
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();

        await SessionCleanup.DeleteQuietlyAsync((_, ct) => Task.Delay(Timeout.Infinite, ct), "session-1", NullLogger.Instance,
            TimeSpan.FromMilliseconds(100)).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Sessions_keep_no_workspace_on_disk()
    {
        var options = new LlmConversationOptions("model", "system", TimeSpan.FromSeconds(5));
        var custom = new CustomModelOptions
        {
            Id = "custom", ProviderType = "openai", BaseUrl = "https://example.invalid/v1", ApiKey = "key", Model = "m",
        };

        Assert.False(CopilotSessionSettings.ForCopilotModel(options).InfiniteSessions!.Enabled);
        Assert.False(CopilotSessionSettings.ForCustomModel(options, custom).InfiniteSessions!.Enabled);
    }
}
