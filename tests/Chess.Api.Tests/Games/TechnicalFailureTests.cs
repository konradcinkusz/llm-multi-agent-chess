using Chess.Api.Games;
using Chess.Api.Players;
using Chess.Api.Tests.Tournaments;
using Chess.Api.Tournaments;
using Chess.Engine;
using Chess.Llm;
using Microsoft.Extensions.Options;
using Xunit;

namespace Chess.Api.Tests.Games;

/// <summary>
/// A gateway that errors on every attempt of a move is not the model's forfeit: the game is aborted, not rated, and the
/// tournament dispatch backs off (docs/RATING.md §2, ADR-017). Timeouts and illegal replies stay forfeits.
/// </summary>
public class TechnicalFailureTests
{
    private static readonly PlayerDescriptor LlmPlayer = new("llm", "fake-a");
    private static readonly PlayerDescriptor RandomPlayer = new("random", "random");

    private static MoveAttempt Failed(int number, string kind, string? raw = null) =>
        new(number, "prompt", raw, null, false, TimeSpan.FromMilliseconds(5), null, null, $"{kind}: reason", RejectionKind: kind);

    /// <summary>A white player that gives up on its first move after the given attempts; black is never asked.</summary>
    private static ScriptedPlayerFactory GivingUp(params MoveAttempt[] attempts) =>
        new((descriptor, _) => new ScriptedPlayer(descriptor,
            (_, _) => Task.FromResult(new PlayerMove(null, attempts, Forfeit: true))));

    private static TechnicalFailureCooldown NewCooldown(TimeProvider clock) =>
        new(Options.Create(new GameRunnerOptions { TechnicalCooldown = TimeSpan.FromSeconds(10), MaxTechnicalCooldown = TimeSpan.FromSeconds(60) }), clock);

    [Fact]
    public async Task Gateway_errors_on_every_attempt_abort_the_game_unrated_and_start_the_cooldown()
    {
        var clock = new ManualClock();
        var cooldown = NewCooldown(clock);
        await using var harness = new GameHarness(
            GivingUp(Failed(1, RejectionClassifier.Error), Failed(2, RejectionClassifier.Error), Failed(3, RejectionClassifier.Error)),
            cooldown: cooldown);
        await harness.StartAsync();
        var gameId = await harness.SeedGameAsync(LlmPlayer, RandomPlayer);

        var events = await harness.PlayToEndAsync(gameId);

        var finished = Assert.IsType<GameFinished>(events[^1]);
        Assert.Equal(GameTermination.Aborted.Termination, finished.Termination);
        Assert.Equal("*", finished.Result);
        var game = await harness.LoadGameAsync(gameId);
        Assert.Equal("aborted", game.Termination);
        Assert.NotNull(game.FinishedAt);
        Assert.Empty(await harness.LoadMovesAsync(gameId));

        // The attempts are kept, so the error log shows what the gateway said.
        var attempts = await harness.LoadAttemptsAsync(gameId);
        Assert.Equal(3, attempts.Count);
        Assert.All(attempts, a => Assert.Equal(RejectionClassifier.Error, a.RejectionKind));
        Assert.True(cooldown.IsHolding);
    }

    [Fact]
    public async Task Timeouts_stay_a_forfeit()
    {
        var cooldown = NewCooldown(new ManualClock());
        await using var harness = new GameHarness(
            GivingUp(Failed(1, RejectionClassifier.Timeout), Failed(2, RejectionClassifier.Timeout), Failed(3, RejectionClassifier.Timeout)),
            cooldown: cooldown);
        await harness.StartAsync();
        var gameId = await harness.SeedGameAsync(LlmPlayer, RandomPlayer);

        var events = await harness.PlayToEndAsync(gameId);

        var finished = Assert.IsType<GameFinished>(events[^1]);
        Assert.Equal("forfeit-white", finished.Termination);
        Assert.Equal("0-1", finished.Result);
        Assert.False(cooldown.IsHolding);
    }

    [Fact]
    public async Task An_error_among_illegal_replies_stays_a_forfeit()
    {
        await using var harness = new GameHarness(
            GivingUp(Failed(1, RejectionClassifier.Error), Failed(2, RejectionClassifier.Illegal, "Qxf7"), Failed(3, RejectionClassifier.Illegal, "e9")));
        await harness.StartAsync();
        var gameId = await harness.SeedGameAsync(LlmPlayer, RandomPlayer);

        var events = await harness.PlayToEndAsync(gameId);

        Assert.Equal("forfeit-white", Assert.IsType<GameFinished>(events[^1]).Termination);
    }

    [Fact]
    public async Task The_real_player_over_a_gateway_that_throws_aborts_the_game()
    {
        var cooldown = NewCooldown(new ManualClock());
        var players = new ChessPlayerFactory(new DownGateway(), Options.Create(new PlayerOptions()));
        await using var harness = new GameHarness(players, cooldown: cooldown);
        await harness.StartAsync();
        var gameId = await harness.SeedGameAsync(LlmPlayer, RandomPlayer);

        var events = await harness.PlayToEndAsync(gameId);

        var finished = Assert.IsType<GameFinished>(events[^1]);
        Assert.Equal("aborted", finished.Termination);
        Assert.Equal("*", finished.Result);
        Assert.Equal(3, (await harness.LoadAttemptsAsync(gameId)).Count);
        Assert.True(cooldown.IsHolding);
    }

    [Fact]
    public async Task A_conversation_that_cannot_be_opened_aborts_the_game_and_starts_the_cooldown()
    {
        var cooldown = NewCooldown(new ManualClock());
        var players = new ChessPlayerFactory(new DownGateway(failToOpen: true), Options.Create(new PlayerOptions()));
        await using var harness = new GameHarness(players, cooldown: cooldown);
        await harness.StartAsync();
        var gameId = await harness.SeedGameAsync(LlmPlayer, RandomPlayer);

        var events = await harness.PlayToEndAsync(gameId);

        Assert.Equal("aborted", Assert.IsType<GameFinished>(events[^1]).Termination);
        Assert.True(cooldown.IsHolding);
    }

    [Fact]
    public async Task A_game_played_to_a_real_end_clears_the_cooldown()
    {
        var cooldown = NewCooldown(new ManualClock());
        cooldown.RecordFailure();
        await using var harness = new GameHarness(GameHarness.RealPlayers(), cooldown: cooldown);
        await harness.StartAsync();
        var gameId = await harness.SeedGameAsync(RandomPlayer, RandomPlayer);

        await harness.PlayToEndAsync(gameId);

        Assert.False(cooldown.IsHolding);
    }

    [Fact]
    public void The_cooldown_doubles_with_each_failure_in_a_row_up_to_its_cap_and_ends_with_time()
    {
        var clock = new ManualClock();
        var cooldown = NewCooldown(clock);
        Assert.False(cooldown.IsHolding);

        cooldown.RecordFailure();
        Assert.Equal(clock.Now + TimeSpan.FromSeconds(10), cooldown.HoldsUntil);

        cooldown.RecordFailure();
        Assert.Equal(clock.Now + TimeSpan.FromSeconds(20), cooldown.HoldsUntil);

        for (var i = 0; i < 10; i++)
        {
            cooldown.RecordFailure();
        }

        Assert.Equal(clock.Now + TimeSpan.FromSeconds(60), cooldown.HoldsUntil);

        clock.Now += TimeSpan.FromSeconds(61);
        Assert.False(cooldown.IsHolding);

        // A success starts the count again.
        cooldown.RecordSuccess();
        cooldown.RecordFailure();
        Assert.Equal(clock.Now + TimeSpan.FromSeconds(10), cooldown.HoldsUntil);
    }

    [Fact]
    public async Task Tournament_dispatch_starts_no_game_while_the_cooldown_holds_and_resumes_after_it()
    {
        await using var harness = new TournamentHarness(cooldown: NewCooldown);
        var created = await harness.CreateAsync(["fake-a", "fake-b", "random"], gamesPerPair: 2, maxConcurrency: 2);
        Assert.Equal(2, harness.Runner.Enqueued.Count);

        // Both running games end in a technical failure, as the runner records them.
        foreach (var gameId in harness.Runner.Held.ToList())
        {
            await harness.FinishAsync(gameId, "*", "aborted");
        }

        harness.Cooldown!.RecordFailure();
        await harness.Tournaments.DispatchAsync(CancellationToken.None);
        Assert.Equal(2, harness.Runner.Enqueued.Count);

        harness.Clock.Now += TimeSpan.FromSeconds(11);
        await harness.Tournaments.DispatchAsync(CancellationToken.None);
        Assert.Equal(4, harness.Runner.Enqueued.Count);
        Assert.Equal(TournamentStatus.Running, (await harness.Tournaments.GetAsync(created.Tournament.Id, CancellationToken.None))!.Status);
    }

    /// <summary>A gateway whose runtime is up but errors on every reply (or, with <paramref name="failToOpen"/>, on every session start).</summary>
    private sealed class DownGateway(bool failToOpen = false) : ILlmGateway
    {
        public LlmGatewayStatus Status { get; } = new(LlmGatewayState.Ready, "ready");

        public Task<IReadOnlyList<LlmModelInfo>> ListModelsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<LlmModelInfo>>([]);

        public Task<ILlmConversation> StartConversationAsync(LlmConversationOptions options, CancellationToken ct) =>
            failToOpen
                ? throw new InvalidOperationException("The runtime is not connected.")
                : Task.FromResult<ILlmConversation>(new FailingConversation());
    }

    private sealed class FailingConversation : ILlmConversation
    {
        public string ModelId => "fake-a";

        public Task<LlmReply> AskAsync(string prompt, CancellationToken ct) =>
            throw new InvalidOperationException("The Copilot runtime is not connected.");

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
