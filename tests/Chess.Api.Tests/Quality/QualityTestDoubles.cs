using Chess.Analysis;
using Chess.Api.Persistence;
using Chess.Api.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Chess.Api.Tests.Quality;

/// <summary>
/// An analyzer with a scripted answer that records each call and the most games in flight at once. The default answer
/// scores ply k with <c>10 * k</c> centipawns and the best move <c>best{k}</c>.
/// </summary>
internal sealed class StubMoveQualityAnalyzer : IMoveQualityAnalyzer
{
    private int _inFlight;

    public Func<string, IReadOnlyList<string>, IReadOnlyList<MoveQuality>> Answer { get; set; } = DefaultAnswer;

    public List<string> AnalyzedStartFens { get; } = [];

    public int MaxInFlight { get; private set; }

    public Action? BeforeAnswer { get; set; }

    public bool IsAvailable { get; set; } = true;

    public async Task<IReadOnlyList<MoveQuality>> AnalyzeAsync(string startFen, IReadOnlyList<string> sanMoves, CancellationToken ct)
    {
        var inFlight = Interlocked.Increment(ref _inFlight);
        MaxInFlight = Math.Max(MaxInFlight, inFlight);
        try
        {
            lock (AnalyzedStartFens)
            {
                AnalyzedStartFens.Add(startFen);
            }

            BeforeAnswer?.Invoke();
            ct.ThrowIfCancellationRequested();
            await Task.Yield();
            return Answer(startFen, sanMoves);
        }
        finally
        {
            Interlocked.Decrement(ref _inFlight);
        }
    }

    public Task<int?> EvaluateAsync(string fen, CancellationToken ct) => Task.FromResult<int?>(null);

    public static IReadOnlyList<MoveQuality> DefaultAnswer(string startFen, IReadOnlyList<string> sanMoves) =>
        sanMoves.Select((_, k) => new MoveQuality(k + 1, 10 * (k + 1), $"best{k + 1}")).ToList();
}

/// <summary>Seeds finished and unfinished games with their moves, over the InMemory provider.</summary>
internal static class QualitySeed
{
    public static readonly DateTimeOffset T0 = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    public static ArenaDbContext NewContext(string databaseName) =>
        new(new DbContextOptionsBuilder<ArenaDbContext>().UseInMemoryDatabase(databaseName).Options);

    /// <summary>Adds a game with one move per SAN, scored or not. A null <paramref name="finishedAt"/> leaves it unfinished.</summary>
    public static async Task AddGameAsync(
        string databaseName, Guid id, string startFen, IReadOnlyList<string> sans, DateTimeOffset? finishedAt, bool scored = false)
    {
        await using var db = NewContext(databaseName);
        db.Games.Add(new GameEntity
        {
            Id = id, StartFen = startFen, CurrentFen = startFen, PlyCount = sans.Count, Result = "1-0",
            CreatedAt = T0, StartedAt = T0, FinishedAt = finishedAt,
        });
        for (var k = 0; k < sans.Count; k++)
        {
            db.Moves.Add(new MoveEntity
            {
                GameId = id, Ply = k + 1, Color = k % 2 == 0 ? "white" : "black", San = sans[k], FenAfter = $"fen{k + 1}",
                Attempts = 1, LatencyMs = 1, PlayedAt = T0,
                CentipawnLoss = scored ? 0 : null, BestSan = scored ? "old" : null,
            });
        }

        await db.SaveChangesAsync();
    }

    public static async Task<Dictionary<(Guid GameId, int Ply), MoveEntity>> LoadMovesAsync(string databaseName)
    {
        await using var db = NewContext(databaseName);
        return await db.Moves.AsNoTracking().ToDictionaryAsync(m => (m.GameId, m.Ply));
    }
}
