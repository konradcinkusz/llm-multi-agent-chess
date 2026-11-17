using System.Diagnostics;
using Chess.Analysis;
using Xunit;

namespace Chess.Api.Tests.Analysis;

/// <summary>
/// The process wrapper against shell scripts that play a healthy, a silent, a hanging and a dying engine. No Stockfish needed.
/// </summary>
public class ProcessUciEngineTests
{
    private const string StartFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    private static readonly TimeSpan Retry = TimeSpan.FromSeconds(30);

    private static readonly UciTimeouts Quick = new(
        Handshake: TimeSpan.FromMilliseconds(400), Search: TimeSpan.FromMilliseconds(600), RetryAfter: Retry);

    // A search that takes seconds to finish must not be mistaken for a slow machine in the healthy cases.
    private static readonly UciTimeouts Roomy = new(
        Handshake: TimeSpan.FromSeconds(20), Search: TimeSpan.FromSeconds(20), RetryAfter: Retry);

    [UnixFact]
    public async Task A_working_binary_completes_the_handshake_and_answers_a_search()
    {
        using var binary = FakeUciBinary.Working();
        await using var engine = new ProcessUciEngine(binary.Path, Roomy);

        await engine.EnsureReadyAsync(CancellationToken.None);
        var search = await engine.SearchAsync(StartFen, ["e2e4"], 5, CancellationToken.None);

        Assert.True(engine.IsAvailable);
        Assert.Equal(new UciSearch(12, "e2e4"), search);
    }

    [UnixFact]
    public async Task A_binary_that_never_answers_the_handshake_times_out_and_is_unavailable()
    {
        using var binary = FakeUciBinary.Silent();
        var time = new ManualTime();
        await using var engine = new ProcessUciEngine(binary.Path, Quick, time);
        var clock = Stopwatch.StartNew();

        var ex = await Assert.ThrowsAsync<TimeoutException>(() => engine.EnsureReadyAsync(CancellationToken.None));

        Assert.Contains("UCI handshake", ex.Message);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), $"took {clock.Elapsed}");
        Assert.False(engine.IsAvailable);
    }

    [UnixFact]
    public async Task While_unavailable_a_call_fails_at_once_and_after_the_retry_delay_a_fresh_process_is_tried()
    {
        using var binary = FakeUciBinary.Silent();
        var time = new ManualTime();
        await using var engine = new ProcessUciEngine(binary.Path, Quick, time);
        await Assert.ThrowsAsync<TimeoutException>(() => engine.EnsureReadyAsync(CancellationToken.None));

        var clock = Stopwatch.StartNew();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => engine.SearchAsync(StartFen, [], 5, CancellationToken.None));
        Assert.Contains("unavailable", ex.Message);
        Assert.True(clock.Elapsed < Quick.Handshake, "a call inside the retry delay must not wait for another timeout");

        time.Advance(Retry + TimeSpan.FromSeconds(1));

        Assert.True(engine.IsAvailable);
        await Assert.ThrowsAsync<TimeoutException>(() => engine.EnsureReadyAsync(CancellationToken.None));
        Assert.False(engine.IsAvailable);
    }

    [UnixFact]
    public async Task A_binary_that_exits_instead_of_answering_surfaces_the_failure()
    {
        using var binary = FakeUciBinary.ExitsAtOnce();
        await using var engine = new ProcessUciEngine(binary.Path, Roomy);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => engine.EnsureReadyAsync(CancellationToken.None));

        Assert.Contains("uciok", ex.Message);
        Assert.False(engine.IsAvailable);
    }

    [UnixFact]
    public async Task A_file_the_system_cannot_run_surfaces_the_failure()
    {
        using var binary = FakeUciBinary.NotExecutable();
        await using var engine = new ProcessUciEngine(binary.Path, Roomy);

        await Assert.ThrowsAnyAsync<Exception>(() => engine.EnsureReadyAsync(CancellationToken.None));

        Assert.False(engine.IsAvailable);
    }

    [UnixFact]
    public async Task A_search_that_does_not_finish_is_killed_and_the_next_search_starts_a_fresh_process()
    {
        using var binary = FakeUciBinary.HangsInTheFirstSearchOnly();
        var time = new ManualTime();
        await using var engine = new ProcessUciEngine(binary.Path, new UciTimeouts(Roomy.Handshake, Quick.Search, Retry), time);
        var clock = Stopwatch.StartNew();

        var ex = await Assert.ThrowsAsync<TimeoutException>(() => engine.SearchAsync(StartFen, [], 5, CancellationToken.None));

        Assert.Contains("depth-5 search", ex.Message);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), $"took {clock.Elapsed}");
        Assert.False(engine.IsAvailable);

        time.Advance(Retry + TimeSpan.FromSeconds(1));
        var search = await engine.SearchAsync(StartFen, [], 5, CancellationToken.None);

        Assert.Equal(new UciSearch(12, "e2e4"), search);
        Assert.True(engine.IsAvailable);
    }

    [UnixFact]
    public async Task A_search_cancelled_by_the_caller_is_not_a_failure_of_the_engine()
    {
        using var binary = FakeUciBinary.HangsInEverySearch();
        await using var engine = new ProcessUciEngine(binary.Path, Roomy);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => engine.SearchAsync(StartFen, [], 5, cancel.Token));

        Assert.True(engine.IsAvailable);
    }

    [UnixFact]
    public async Task The_analyzer_reports_a_hung_engine_as_an_exception_and_as_unavailable()
    {
        using var binary = FakeUciBinary.HangsInEverySearch();
        await using var analyzer = new StockfishAnalyzer(binary.Path, depth: 5, new UciTimeouts(Roomy.Handshake, Quick.Search, Retry));

        Assert.True(analyzer.IsAvailable);
        await Assert.ThrowsAsync<TimeoutException>(() => analyzer.EvaluateAsync(StartFen, CancellationToken.None));

        Assert.False(analyzer.IsAvailable);
    }

    [UnixFact]
    public async Task The_analyzer_probe_succeeds_for_a_working_binary_and_throws_for_a_silent_one()
    {
        using var working = FakeUciBinary.Working();
        using var silent = FakeUciBinary.Silent();
        await using var good = new StockfishAnalyzer(working.Path, depth: 5, Roomy);
        await using var bad = new StockfishAnalyzer(silent.Path, depth: 5, Quick);

        await good.ProbeAsync(CancellationToken.None);
        await Assert.ThrowsAsync<TimeoutException>(() => bad.ProbeAsync(CancellationToken.None));

        Assert.True(good.IsAvailable);
        Assert.False(bad.IsAvailable);
    }

    [Fact]
    public void Timeouts_must_be_positive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ProcessUciEngine("stockfish", new UciTimeouts(TimeSpan.Zero, TimeSpan.FromSeconds(1), Retry)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ProcessUciEngine("stockfish", new UciTimeouts(TimeSpan.FromSeconds(1), TimeSpan.Zero, Retry)));
    }

    private sealed class ManualTime : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
