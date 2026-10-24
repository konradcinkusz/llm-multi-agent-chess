using Chess.Api.Persistence;
using Chess.Api.Persistence.Entities;
using Chess.Rating;
using Microsoft.EntityFrameworkCore;

namespace Chess.Api.Tests.Ratings;

/// <summary>
/// A small game set for the InMemory provider. The expectations are worked out by hand from RATING.md §1, §2 and §4,
/// so the tests compare the code against the rules rather than against its own output.
/// </summary>
internal static class RatingSeed
{
    public static readonly DateTimeOffset T0 = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    public static readonly Guid Spring = Guid.Parse("00000000-0000-0000-0000-000000000100");

    // Rated and finished. G1 and G5 belong to Spring.
    public static readonly Guid G1 = Guid.Parse("00000000-0000-0000-0000-000000000001");
    public static readonly Guid G2 = Guid.Parse("00000000-0000-0000-0000-000000000002");
    public static readonly Guid G3 = Guid.Parse("00000000-0000-0000-0000-000000000003");
    public static readonly Guid G4 = Guid.Parse("00000000-0000-0000-0000-000000000004");
    public static readonly Guid G5 = Guid.Parse("00000000-0000-0000-0000-000000000005");

    // Excluded: unrated, interrupted (result *), and still queued (result *).
    public static readonly Guid Unrated = Guid.Parse("00000000-0000-0000-0000-000000000006");
    public static readonly Guid Interrupted = Guid.Parse("00000000-0000-0000-0000-000000000007");
    public static readonly Guid Queued = Guid.Parse("00000000-0000-0000-0000-000000000008");

    /// <summary>Outcomes of G1–G5 in game-id order (White's score: 1, ½, 0).</summary>
    public static readonly IReadOnlyList<GameOutcome> RatedOutcomes =
    [
        new("fake-a", "fake-b", 1.0),    // G1 1-0
        new("fake-b", "fake-a", 1.0),    // G2 1-0, fake-a is Black and loses
        new("fake-a", "random", 0.5),    // G3 1/2-1/2
        new("random", "fake-b", 0.0),    // G4 0-1, fake-b wins as Black
        new("fake-a", "fake-b", 0.0),    // G5 0-1
    ];

    /// <summary>
    /// Move statistics over G1–G5 (RATING.md §4). fake-a: four first-attempt moves, cp losses 20, 10, 0, 30 → ACPL 15.
    /// fake-b: G2 and G4 have moves without a cp loss, so its ACPL is unknown. random: legal by construction.
    /// </summary>
    public static readonly IReadOnlyList<PlayerMoveStats> RatedStats =
    [
        new("fake-a", 4, 4, 15),
        new("fake-b", 4, 2, null),
        new("random", 2, 2, null),
    ];

    /// <summary>Outcomes of the Spring games G1 and G5 only.</summary>
    public static readonly IReadOnlyList<GameOutcome> SpringOutcomes =
    [
        new("fake-a", "fake-b", 1.0),
        new("fake-a", "fake-b", 0.0),
    ];

    /// <summary>Spring move statistics: fake-a cp losses 20 and 30 → 25; fake-b cp losses 40 and 50 → 45.</summary>
    public static readonly IReadOnlyList<PlayerMoveStats> SpringStats =
    [
        new("fake-a", 2, 2, 25),
        new("fake-b", 2, 1, 45),
    ];

    public static void Populate(ArenaDbContext db)
    {
        db.Tournaments.Add(new TournamentEntity
        {
            Id = Spring, Name = "Spring", Models = ["fake-a", "fake-b", "random"], GamesPerPair = 1,
            MaxConcurrency = 1, Status = "finished", CreatedAt = T0, FinishedAt = T0.AddHours(1),
        });

        db.Games.AddRange(
            Game(G1, "fake-a", "fake-b", "1-0", Spring),
            Game(G2, "fake-b", "fake-a", "1-0"),
            Game(G3, "fake-a", "random", "1/2-1/2"),
            Game(G4, "random", "fake-b", "0-1"),
            Game(G5, "fake-a", "fake-b", "0-1", Spring),
            Game(Unrated, "fake-a", "fake-b", "0-1", rated: false),
            Game(Interrupted, "fake-a", "fake-b", "*", termination: "interrupted"),
            Game(Queued, "fake-b", "fake-a", "*"));

        db.Moves.AddRange(
            Move(G1, 1, "white", 1, 20), Move(G1, 2, "black", 2, 40),
            Move(G2, 1, "white", 1, null), Move(G2, 2, "black", 1, 10),
            Move(G3, 1, "white", 1, 0), Move(G3, 2, "black", 1, null),
            Move(G4, 1, "white", 1, null), Move(G4, 2, "black", 3, null),
            Move(G5, 1, "white", 1, 30), Move(G5, 2, "black", 1, 50),
            // Moves of excluded games must not reach the statistics: 3 attempts and a huge loss would show if they did.
            Move(Unrated, 1, "white", 3, 999), Move(Unrated, 2, "black", 3, 999),
            Move(Interrupted, 1, "white", 3, 999));
    }

    /// <summary>A single finished, rated game, for tests that add to the set after it was seeded.</summary>
    public static GameEntity Game(Guid id, string white, string black, string result, Guid? tournament = null, bool rated = true, string? termination = null) => new()
    {
        Id = id,
        TournamentId = tournament,
        Rated = rated,
        Result = result,
        Termination = termination,
        WhiteKind = KindOf(white),
        WhiteModel = white,
        BlackKind = KindOf(black),
        BlackModel = black,
        StartFen = "",
        CurrentFen = "",
        CreatedAt = T0,
        FinishedAt = result == "*" ? null : T0.AddMinutes(5),
    };

    /// <summary>A factory over a fresh InMemory store that already holds the seeded set.</summary>
    public static IDbContextFactory<ArenaDbContext> SeededFactory(string databaseName)
    {
        var options = new DbContextOptionsBuilder<ArenaDbContext>().UseInMemoryDatabase(databaseName).Options;
        using (var db = new ArenaDbContext(options))
        {
            Populate(db);
            db.SaveChanges();
        }

        return new OptionsFactory(options);
    }

    /// <summary>Hands out a new context over the same InMemory store on every call, as the pooled factory does.</summary>
    internal sealed class OptionsFactory(DbContextOptions<ArenaDbContext> options) : IDbContextFactory<ArenaDbContext>
    {
        public ArenaDbContext CreateDbContext() => new(options);
    }

    private static string KindOf(string model) => model == "random" ? "random" : "llm";

    private static MoveEntity Move(Guid gameId, int ply, string color, int attempts, int? centipawnLoss) => new()
    {
        GameId = gameId,
        Ply = ply,
        Color = color,
        San = "e4",
        FenAfter = "",
        Attempts = attempts,
        LatencyMs = 100,
        CentipawnLoss = centipawnLoss,
        PlayedAt = T0,
    };
}
