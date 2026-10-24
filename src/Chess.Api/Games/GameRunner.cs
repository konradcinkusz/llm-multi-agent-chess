using System.Collections.Concurrent;
using System.Threading.Channels;
using Chess.Analysis;
using Chess.Api.Persistence;
using Chess.Api.Persistence.Entities;
using Chess.Api.Players;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Chess.Api.Games;

/// <summary>
/// Plays queued games to their end (03 §3.3, §7). A bounded queue feeds <c>Arena:MaxConcurrentGames</c> workers, so
/// that many games run at once and no more. A game that fails, or is aborted, is recorded as <c>aborted</c>, and its
/// worker goes on with the next game. A game cut off by the host stopping is left started; the start-up sweep closes it.
/// Once the schema is ready, the games saved as queued but never started are queued again, oldest first, because the
/// queue itself is lost with the process.
/// </summary>
public sealed class GameRunner(
    IDbContextFactory<ArenaDbContext> factory,
    IChessPlayerFactory players,
    IMoveQualityAnalyzer analyzer,
    IGameEventStream events,
    IOptions<GameRunnerOptions> options,
    TimeProvider clock,
    DatabaseReadiness readiness,
    ILogger<GameRunner> logger) : BackgroundService, IGameRunner
{
    private const int QueueCapacity = 1024;

    private readonly Channel<Guid> _queue = Channel.CreateBounded<Guid>(QueueCapacity);
    private readonly ConcurrentDictionary<Guid, GameSlot> _slots = new();

    public async ValueTask EnqueueAsync(Guid gameId, CancellationToken ct)
    {
        // A game that is already queued or running is not queued twice.
        if (!_slots.TryAdd(gameId, new GameSlot()))
        {
            return;
        }

        try
        {
            await _queue.Writer.WriteAsync(gameId, ct);
        }
        catch
        {
            _slots.TryRemove(gameId, out _);
            throw;
        }
    }

    public bool TryAbort(Guid gameId)
    {
        if (!_slots.TryGetValue(gameId, out var slot))
        {
            return false;
        }

        slot.Abort();
        return true;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(options.Value.MaxConcurrentGames, 1);

        var workers = Enumerable.Range(0, options.Value.MaxConcurrentGames)
            .Select(_ => WorkAsync(stoppingToken))
            .Append(RequeueQueuedGamesAsync(stoppingToken))
            .ToArray();
        await Task.WhenAll(workers);
    }

    /// <summary>
    /// Waits for the schema, then queues every game that was saved but never started. Such a game is not in the queue
    /// after a restart, so nothing else would play it. A game that is already queued or running is not queued twice.
    /// </summary>
    private async Task RequeueQueuedGamesAsync(CancellationToken stoppingToken)
    {
        try
        {
            await readiness.Finished.WaitAsync(stoppingToken);
            if (readiness.Current.State != DatabaseState.Ready)
            {
                logger.LogWarning("The database is not ready, so queued games are not requeued");
                return;
            }

            var queued = await QueuedGameIdsAsync(stoppingToken);
            foreach (var gameId in queued)
            {
                await EnqueueAsync(gameId, stoppingToken);
            }

            logger.LogInformation("Requeued {Count} game(s) that were queued before the start", queued.Count);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The host is stopping; the games stay queued in the database.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Queued games could not be requeued");
        }
    }

    private async Task<List<Guid>> QueuedGameIdsAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Games
            .Where(g => g.StartedAt == null && g.FinishedAt == null)
            .OrderBy(g => g.CreatedAt)
            .Select(g => g.Id)
            .ToListAsync(ct);
    }

    private async Task WorkAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var gameId in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                await RunGameAsync(gameId, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The host is stopping; queued games stay queued.
        }
    }

    /// <summary>
    /// Plays one game to its end in the caller's task, outside the queue, for a broadcast slot (03 §9.1). The game is
    /// aborted when <paramref name="abort"/> is cancelled and recorded as aborted, as a queued game is.
    /// </summary>
    /// <returns>
    /// The end that was recorded, or null when the game was not played to an end: it could not be loaded or played, or
    /// the host is stopping (the game is then left started, and the start-up sweep closes it).
    /// </returns>
    public async Task<GameEnd?> PlayToEndAsync(Guid gameId, GameRunHooks? hooks, CancellationToken abort, CancellationToken stoppingToken)
    {
        var slot = new GameSlot();
        if (!_slots.TryAdd(gameId, slot))
        {
            throw new InvalidOperationException($"Game {gameId} is already queued or running.");
        }

        using var registration = abort.Register(slot.Abort);
        try
        {
            return await PlayGameAsync(gameId, slot, hooks, stoppingToken);
        }
        finally
        {
            _slots.TryRemove(gameId, out _);
        }
    }

    private async Task RunGameAsync(Guid gameId, CancellationToken stoppingToken)
    {
        try
        {
            await PlayGameAsync(gameId, _slots[gameId], null, stoppingToken);
        }
        finally
        {
            _slots.TryRemove(gameId, out _);
        }
    }

    /// <returns>The end that was recorded, or null when the game was not played to an end (see <see cref="PlayToEndAsync"/>).</returns>
    private async Task<GameEnd?> PlayGameAsync(Guid gameId, GameSlot slot, GameRunHooks? hooks, CancellationToken stoppingToken)
    {
        // Only the play itself is cancelled by an abort, so a game aborted while queued is still recorded as aborted.
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, slot.Token);
        try
        {
            await using var db = await factory.CreateDbContextAsync(stoppingToken);
            var game = await db.Games.FirstOrDefaultAsync(g => g.Id == gameId, stoppingToken);
            if (game is null || game.FinishedAt is not null)
            {
                logger.LogWarning("Game {GameId} is missing or already finished; not playing it", gameId);
                return null;
            }

            var run = new GameRun(db, game, hooks?.Players ?? players, analyzer, events, options.Value, clock, hooks?.Pacer);
            var end = await PlayOrEndAsync(run, gameId, slot, stoppingToken, cancellation.Token);
            if (end is null)
            {
                return null;
            }

            await FinishAsync(run, end, gameId, stoppingToken);
            return end;
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Game {GameId} could not be played", gameId);
            return null;
        }
    }

    /// <returns>The end to record, or null when the host is stopping and the game must be left as it is.</returns>
    private async Task<GameEnd?> PlayOrEndAsync(
        GameRun run, Guid gameId, GameSlot slot, CancellationToken stoppingToken, CancellationToken ct)
    {
        try
        {
            return await run.PlayAsync(ct);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogWarning("Game {GameId} was cut off by the host stopping; it is closed as interrupted at the next start", gameId);
            return null;
        }
        catch (OperationCanceledException) when (slot.AbortRequested)
        {
            logger.LogInformation("Game {GameId} was aborted", gameId);
            return GameTermination.Aborted;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Game {GameId} failed and is closed as aborted", gameId);
            return GameTermination.Aborted;
        }
    }

    private async Task FinishAsync(GameRun run, GameEnd end, Guid gameId, CancellationToken stoppingToken)
    {
        try
        {
            await run.FinishAsync(end, stoppingToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Game {GameId} ended as {Termination} but the end could not be saved", gameId, end.Termination);
        }
        finally
        {
            // Subscribers must not wait forever, even when the save failed.
            events.Publish(new GameFinished(gameId, clock.GetUtcNow(), end.Result, end.Termination));
        }
    }

    /// <summary>A queued or running game: its cancellation, and whether it was asked to stop by an abort.</summary>
    private sealed class GameSlot
    {
        private readonly CancellationTokenSource _cancellation = new();
        private volatile bool _abortRequested;

        public CancellationToken Token => _cancellation.Token;

        public bool AbortRequested => _abortRequested;

        public void Abort()
        {
            _abortRequested = true;
            _cancellation.Cancel();
        }
    }
}
