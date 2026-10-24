using Chess.Api.Persistence;
using Chess.Api.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Chess.Api.Tests.Results;

/// <summary>Rows written straight into the InMemory provider, plus a clock that moves on each read.</summary>
internal static class ResultsFixtures
{
    public static readonly DateTimeOffset T0 = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    // FEN side-to-move after the move: "b" means White just moved (03 §4, fen_after).
    public const string AfterWhiteMove = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1";
    public const string AfterBlackMove = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2";

    public static ArenaDbContext NewContext(string databaseName) =>
        new(new DbContextOptionsBuilder<ArenaDbContext>().UseInMemoryDatabase(databaseName).Options);

    public static GameEntity Game(
        Guid id, string white, string black, string result, string? termination, DateTimeOffset? finishedAt,
        bool rated = true, Guid? tournamentId = null) => new()
    {
        Id = id,
        TournamentId = tournamentId,
        WhiteKind = KindOf(white),
        WhiteModel = white,
        BlackKind = KindOf(black),
        BlackModel = black,
        Rated = rated,
        Result = result,
        Termination = termination,
        CreatedAt = T0,
        StartedAt = T0,
        FinishedAt = finishedAt,
    };

    public static MoveEntity Move(
        Guid gameId, int ply, string fenAfter, int attempts, long latencyMs,
        int? inputTokens = null, int? outputTokens = null, int? centipawnLoss = null) => new()
    {
        GameId = gameId,
        Ply = ply,
        Color = "",
        San = "e4",
        FenAfter = fenAfter,
        Attempts = attempts,
        LatencyMs = latencyMs,
        InputTokens = inputTokens,
        OutputTokens = outputTokens,
        CentipawnLoss = centipawnLoss,
        PlayedAt = T0,
    };

    public static RatingSnapshotEntity Snapshot(
        Guid afterGameId, DateTimeOffset takenAt, int ratedGames, string model, double rating) => new()
    {
        TakenAt = takenAt,
        AfterGameId = afterGameId,
        RatedGames = ratedGames,
        Model = model,
        Rating = rating,
        CiLow = rating - 50,
        CiHigh = rating + 50,
        ArenaScore = 50,
        Games = ratedGames,
        Provisional = true,
        FormulaVersion = "1",
    };

    /// <summary>
    /// Tournament <paramref name="tournamentId"/>: A and B play four decided games (one checkmate, one stalemate,
    /// two forfeits) and one unfinished game. A sixth game, outside the tournament, is decided and A wins it.
    /// </summary>
    public static class BreakdownFixture
    {
        public static readonly Guid Tournament = Guid.Parse("7a1c0f54-3b9e-4d2a-9c61-2f0e8d4b1a01");
        public static readonly Guid Game1 = Guid.Parse("00000000-0000-0000-0000-000000000001");
        public static readonly Guid Game2 = Guid.Parse("00000000-0000-0000-0000-000000000002");
        public static readonly Guid Game3 = Guid.Parse("00000000-0000-0000-0000-000000000003");
        public static readonly Guid Game4 = Guid.Parse("00000000-0000-0000-0000-000000000004");
        public static readonly Guid Unfinished = Guid.Parse("00000000-0000-0000-0000-000000000005");
        public static readonly Guid Outside = Guid.Parse("00000000-0000-0000-0000-000000000006");

        public static void Seed(ArenaDbContext db)
        {
            db.Tournaments.Add(new TournamentEntity
            {
                Id = Tournament,
                Name = "Fixture",
                Models = ["A", "B"],
                GamesPerPair = 2,
                MaxConcurrency = 1,
                Status = "finished",
                CreatedAt = T0,
            });

            db.Games.AddRange(
                Game(Game1, "A", "B", "1-0", "checkmate", T0.AddMinutes(1), tournamentId: Tournament),
                Game(Game2, "B", "A", "1/2-1/2", "stalemate", T0.AddMinutes(2), tournamentId: Tournament),
                Game(Game3, "A", "B", "0-1", "forfeit-white", T0.AddMinutes(3), tournamentId: Tournament),
                Game(Game4, "B", "A", "1-0", "forfeit-black", T0.AddMinutes(4), tournamentId: Tournament),
                Game(Unfinished, "A", "B", "*", null, null, tournamentId: Tournament),
                Game(Outside, "A", "B", "1-0", "checkmate", T0.AddMinutes(5)));

            db.Moves.AddRange(
                // A (White) plays a first-attempt legal move, with tokens and an evaluation.
                Move(Game1, 1, AfterWhiteMove, attempts: 1, latencyMs: 100, inputTokens: 10, outputTokens: 5, centipawnLoss: 20),
                // B (Black) needs a second attempt.
                Move(Game1, 2, AfterBlackMove, attempts: 2, latencyMs: 300),
                // B (White) plays first time.
                Move(Game2, 1, AfterWhiteMove, attempts: 1, latencyMs: 50),
                // A (White) needs three attempts; no tokens or evaluation recorded.
                Move(Game3, 1, AfterWhiteMove, attempts: 3, latencyMs: 200));
        }
    }

    private static string KindOf(string model) => model == "random" ? "random" : "llm";
}

/// <summary>A clock that returns a later instant on every read, so batches have distinct <c>taken_at</c>.</summary>
internal sealed class StepClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _next = start;

    public override DateTimeOffset GetUtcNow()
    {
        var now = _next;
        _next = _next.AddMinutes(1);
        return now;
    }
}
