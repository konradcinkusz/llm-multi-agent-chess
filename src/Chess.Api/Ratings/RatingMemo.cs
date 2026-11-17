using System.Collections.Concurrent;
using Chess.Api.Persistence;
using Chess.Rating;
using Microsoft.EntityFrameworkCore;

namespace Chess.Api.Ratings;

/// <summary>The rated games of one scope, their report and every ordered pair of its models (the matrix).</summary>
public sealed record RatingView(RatedGames Rated, RatingReport Report, IReadOnlyList<PairwiseCoefficient> Matrix);

/// <summary>
/// Remembers the last <see cref="RatingView"/> of each scope (all games, or one tournament) so that the leaderboard, the
/// matrix and the pairwise reading of one page load are computed once, not once per request.
/// <para>
/// This is a memo of a pure function of the games and moves tables, not a stored leaderboard: it holds nothing the next
/// request could not recompute from the tables, it is dropped on restart, it is keyed by the data's
/// <see cref="RatedDataVersion"/> so any finished rated game or move annotation replaces it, and it is never written to
/// <c>rating_snapshots</c> or read from them (CLAUDE.md: ratings are never stored as the leaderboard).
/// </para>
/// <para>
/// One computation runs per scope at a time. A request that arrives while it runs waits for it instead of starting a second
/// fit, and a request that gives up (its token is cancelled) stops its own wait or fit without discarding a finished result.
/// </para>
/// </summary>
public sealed class RatingMemo
{
    // Scopes held at once. A tournament id comes from the client, so the memo must not grow with the ids it is asked about.
    internal const int MaxScopes = 16;

    private readonly ConcurrentDictionary<Guid, Scope> scopes = new();
    private readonly object evicting = new();
    private long clock;

    internal int ScopeCount => scopes.Count;

    private sealed record Entry(RatedDataVersion Version, RatingView View);

    private sealed class Scope
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public Entry? Current;
        public long LastUsed { get; set; }
    }

    /// <param name="tournamentId">Only that tournament's games, or every rated game when null.</param>
    public async Task<RatingView> GetAsync(IDbContextFactory<ArenaDbContext> factory, Guid? tournamentId, CancellationToken ct)
    {
        // Read the version before the data: a change during the load leaves the entry one version behind, so the
        // next request refreshes it instead of trusting data that may predate the change.
        var version = await RatedGameLoader.VersionAsync(factory, tournamentId, ct);

        var scope = scopes.GetOrAdd(tournamentId ?? Guid.Empty, _ => new Scope());
        scope.LastUsed = Interlocked.Increment(ref clock);
        if (Volatile.Read(ref scope.Current) is { } fresh && fresh.Version == version)
        {
            return fresh.View;
        }

        await scope.Gate.WaitAsync(ct);
        try
        {
            if (scope.Current is { } computed && computed.Version == version)
            {
                return computed.View;
            }

            var rated = await RatedGameLoader.LoadAsync(factory, tournamentId, ct);
            var both = RatingCalculator.ComputeWithMatrix(rated.Outcomes, rated.MoveStats, new RatingOptions(), ct);
            var view = new RatingView(rated, both.Report, both.Matrix);
            Volatile.Write(ref scope.Current, new Entry(version, view));
            Evict();
            return view;
        }
        finally
        {
            scope.Gate.Release();
        }
    }

    private void Evict()
    {
        lock (evicting)
        {
            while (scopes.Count > MaxScopes)
            {
                var oldest = scopes.MinBy(entry => entry.Value.LastUsed);
                scopes.TryRemove(oldest.Key, out _);
            }
        }
    }
}
