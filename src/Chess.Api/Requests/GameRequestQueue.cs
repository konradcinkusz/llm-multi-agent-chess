using Chess.Api.Contracts;
using Chess.Api.Persistence;
using Chess.Api.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Chess.Api.Requests;

/// <summary>
/// The FIFO of paid game requests (03 §15.1, §15.4). The broadcast runner takes one at a time with <see cref="TakeNextAsync"/>.
/// A request is played at most twice: a failure requeues it once, then credits the payer.
/// </summary>
public sealed class GameRequestQueue(IDbContextFactory<ArenaDbContext> factory, IOptions<RequestOptions> options, TimeProvider clock)
    : IGameRequestQueue
{
    /// <summary>The attempts a request gets before it is credited: requeued once, then credited (03 §15.4).</summary>
    public const int MaxAttempts = 2;

    /// <summary>How many recent requests the queue reports.</summary>
    public const int RecentCount = 10;

    private const int TakeAttempts = 5;

    public async Task<QueuedRequest?> TakeNextAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        for (var attempt = 0; attempt < TakeAttempts; attempt++)
        {
            var next = await db.GameRequests.AsNoTracking()
                .Where(r => r.Status == RequestStatus.Queued)
                .OrderBy(r => r.ReceivedAt).ThenBy(r => r.Id)
                .FirstOrDefaultAsync(ct);
            if (next is null)
            {
                return null;
            }

            // Another taker may have won this row between the read and the update; then the next candidate is tried.
            if (!await TryMarkPlayingAsync(db, next.Id, clock.GetUtcNow(), ct))
            {
                continue;
            }

            var taken = await db.GameRequests.AsNoTracking().FirstAsync(r => r.Id == next.Id, ct);
            return new QueuedRequest(taken.Id, taken.White, taken.Black, Money.ToUnits(taken.PriceMicros),
                Money.ToUnits(taken.PriceMicros) * (1 - options.Value.FeeShare), taken.PayerDisplay);
        }

        return null;
    }

    public async Task StartedAsync(Guid requestId, Guid gameId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var request = await db.GameRequests.FirstAsync(r => r.Id == requestId, ct);
        request.GameId = gameId;
        await db.SaveChangesAsync(ct);
    }

    public async Task PlayedAsync(Guid requestId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var request = await db.GameRequests.FirstAsync(r => r.Id == requestId, ct);
        if (request.Status != RequestStatus.Playing)
        {
            return;
        }

        request.Status = RequestStatus.Played;
        request.FinishedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
    }

    public async Task FailedAsync(Guid requestId, string reason, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var request = await db.GameRequests.FirstAsync(r => r.Id == requestId, ct);
        if (request.Status != RequestStatus.Playing)
        {
            return;
        }

        var now = clock.GetUtcNow();
        request.Reason = reason;
        if (request.Attempts < MaxAttempts)
        {
            request.Status = RequestStatus.Queued;
            request.StartedAt = null;
        }
        else
        {
            request.Status = RequestStatus.Failed;
            request.FinishedAt = now;
            await PayerLedger.CreditAsync(db, request, now, ct);
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>The owner cancels a queued request. Its price is credited back. Only a queued request can be cancelled.</summary>
    public async Task<CancelOutcome> CancelAsync(Guid requestId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var request = await db.GameRequests.FirstOrDefaultAsync(r => r.Id == requestId, ct);
        if (request is null)
        {
            return new CancelOutcome(null, Found: false, Error: null);
        }

        if (request.Status != RequestStatus.Queued)
        {
            return new CancelOutcome(null, Found: true, Error: $"The request is {request.Status}; only a queued request can be cancelled.");
        }

        var now = clock.GetUtcNow();
        request.Status = RequestStatus.Cancelled;
        request.Reason = "cancelled by the owner";
        request.FinishedAt = now;
        await PayerLedger.CreditAsync(db, request, now, ct);
        await db.SaveChangesAsync(ct);

        return new CancelOutcome(Map(request, null), Found: true, Error: null);
    }

    /// <summary>The queue as the stream and the owner see it: what plays, what waits and in what order, and what recently finished.</summary>
    public async Task<RequestQueueDto> SnapshotAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var playing = await db.GameRequests.AsNoTracking()
            .Where(r => r.Status == RequestStatus.Playing)
            .OrderBy(r => r.StartedAt).ThenBy(r => r.Id)
            .FirstOrDefaultAsync(ct);

        var queued = await db.GameRequests.AsNoTracking()
            .Where(r => r.Status == RequestStatus.Queued)
            .OrderBy(r => r.ReceivedAt).ThenBy(r => r.Id)
            .ToListAsync(ct);

        var recent = await db.GameRequests.AsNoTracking()
            .Where(r => r.Status != RequestStatus.Queued && r.Status != RequestStatus.Playing)
            .OrderByDescending(r => r.ReceivedAt).ThenByDescending(r => r.Id)
            .Take(RecentCount)
            .ToListAsync(ct);

        // A new request waits for what plays now and for everything queued ahead of it.
        var gamesAhead = queued.Count + (playing is null ? 0 : 1);
        var wait = (int)Math.Ceiling(gamesAhead * options.Value.EstimatedMinutesPerGame);

        return new RequestQueueDto(
            playing is null ? null : Map(playing, null),
            queued.Select((request, index) => Map(request, index + 1)).ToArray(),
            recent.Select(request => Map(request, null)).ToArray(),
            wait);
    }

    /// <summary>Marks a queued request as playing. A conditional update on PostgreSQL; a checked save on the InMemory provider.</summary>
    private static async Task<bool> TryMarkPlayingAsync(ArenaDbContext db, Guid id, DateTimeOffset now, CancellationToken ct)
    {
        if (db.Database.IsRelational())
        {
            // One UPDATE that only matches a queued row: of two takers that chose the same row, exactly one changes it.
            var rows = await db.GameRequests
                .Where(r => r.Id == id && r.Status == RequestStatus.Queued)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(r => r.Status, RequestStatus.Playing)
                    .SetProperty(r => r.StartedAt, now)
                    .SetProperty(r => r.Attempts, r => r.Attempts + 1), ct);
            return rows == 1;
        }

        // The InMemory provider has no bulk update. A single process needs only the status check.
        var request = await db.GameRequests.FirstAsync(r => r.Id == id, ct);
        if (request.Status != RequestStatus.Queued)
        {
            return false;
        }

        request.Status = RequestStatus.Playing;
        request.StartedAt = now;
        request.Attempts++;
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Maps a stored request to its DTO. <paramref name="position"/> is 1-based and set only for a queued request.</summary>
    public static GameRequestDto Map(GameRequestEntity request, int? position) => new(
        request.Id, request.Source, request.PayerDisplay, request.White, request.Black, Money.ToUnits(request.PriceMicros),
        request.Status, request.Reason, request.GameId, request.ReceivedAt, position);
}

/// <summary>The result of a cancel: the request on success; otherwise whether it was found, and why it was refused.</summary>
public sealed record CancelOutcome(GameRequestDto? Request, bool Found, string? Error);
