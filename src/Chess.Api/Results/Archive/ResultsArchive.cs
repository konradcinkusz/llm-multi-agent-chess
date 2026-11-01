using System.Globalization;
using Chess.Api.Persistence;
using Chess.Api.Persistence.Entities;
using Chess.Engine;
using Microsoft.EntityFrameworkCore;

namespace Chess.Api.Results.Archive;

public sealed record ArchiveExport(int Written, int AlreadyThere, string Folder);

public sealed record ArchiveImport(int Added, int AlreadyThere);

/// <summary>
/// Moves rated games between the database and the archive folder. Export only adds files (an existing file is never rewritten);
/// import only adds games (a game id already in the database is left as it is), so both can run again at any time.
/// </summary>
public sealed class ResultsArchive(IDbContextFactory<ArenaDbContext> factory)
{
    /// <summary>The rated, finished games of the database as records, oldest first. A game is archived under a fixed shape: tournament ties and prompts stay local.</summary>
    public async Task<IReadOnlyList<GameRecord>> ReadRecordsAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var games = await db.Games.AsNoTracking()
            .Where(g => g.Rated && g.Result != "*" && g.FinishedAt != null)
            .OrderBy(g => g.FinishedAt).ThenBy(g => g.Id)
            .ToListAsync(ct);
        var ids = games.Select(g => g.Id).ToList();
        var moves = (await db.Moves.AsNoTracking().Where(m => ids.Contains(m.GameId)).ToListAsync(ct))
            .ToLookup(m => m.GameId);

        return games.Select(g => new GameRecord(
            GameRecord.CurrentVersion,
            g.Id,
            new RecordSide(g.WhiteKind, g.WhiteKind == "random" ? "random" : g.WhiteModel),
            new RecordSide(g.BlackKind, g.BlackKind == "random" ? "random" : g.BlackModel),
            g.Result,
            g.Termination ?? "",
            IsStandard(g.StartFen) ? null : g.StartFen,
            g.FinishedAt ?? g.CreatedAt,
            moves[g.Id].OrderBy(m => m.Ply)
                .Select(m => new RecordMove(m.San, m.Attempts, m.LatencyMs, m.InputTokens, m.OutputTokens, m.CentipawnLoss, m.BestSan))
                .ToList())).ToList();
    }

    /// <summary>Writes a file for each game that has none. A game that does not validate is reported, not written.</summary>
    public async Task<ArchiveExport> ExportAsync(string folder, CancellationToken ct)
    {
        int written = 0, there = 0;
        foreach (var record in await ReadRecordsAsync(ct))
        {
            var problems = GameRecordValidator.Validate(record);
            if (problems.Count > 0)
            {
                throw new InvalidOperationException($"Game {record.Id} cannot be archived: {string.Join("; ", problems)}");
            }

            var path = Path.Combine(folder, GameRecordJson.RelativePath(record).Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(path))
            {
                there++;
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, GameRecordJson.Serialize(record), ct);
            written++;
        }

        return new ArchiveExport(written, there, folder);
    }

    /// <summary>Adds the games the database does not hold. The records must already be valid (<see cref="GameRecordFolder.Read"/>).</summary>
    public async Task<ArchiveImport> ImportAsync(IReadOnlyList<GameRecord> records, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var ids = records.Select(r => r.Id).ToList();
        var known = (await db.Games.Where(g => ids.Contains(g.Id)).Select(g => g.Id).ToListAsync(ct)).ToHashSet();

        var added = 0;
        foreach (var record in records.Where(r => !known.Contains(r.Id)))
        {
            var (game, moves) = ToEntities(record);
            db.Games.Add(game);
            db.Moves.AddRange(moves);
            added++;
        }

        await db.SaveChangesAsync(ct);
        return new ArchiveImport(added, records.Count - added);
    }

    private static (GameEntity Game, List<MoveEntity> Moves) ToEntities(GameRecord record)
    {
        var board = string.IsNullOrWhiteSpace(record.StartFen) ? ChessBoard.StartingPosition() : FromFen(record.StartFen);
        var startFen = board.ToFen();
        var moves = new List<MoveEntity>();
        foreach (var (move, index) in record.Moves.Select((m, i) => (m, i)))
        {
            var color = board.SideToMove == Side.White ? "white" : "black";
            board.ApplyMoveSan(move.San);
            moves.Add(new MoveEntity
            {
                GameId = record.Id,
                Ply = index + 1,
                Color = color,
                San = move.San,
                FenAfter = board.ToFen(),
                Attempts = move.Attempts,
                LatencyMs = move.LatencyMs,
                InputTokens = move.InputTokens,
                OutputTokens = move.OutputTokens,
                CentipawnLoss = move.CentipawnLoss,
                BestSan = move.BestSan,
                PlayedAt = record.PlayedAt,
            });
        }

        var tags = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Event"] = "LLM Chess Arena",
            ["Date"] = record.PlayedAt.UtcDateTime.ToString("yyyy.MM.dd", CultureInfo.InvariantCulture),
            ["White"] = record.White.Model,
            ["Black"] = record.Black.Model,
        };
        var game = new GameEntity
        {
            Id = record.Id,
            WhiteKind = record.White.Kind,
            WhiteModel = record.White.Model,
            BlackKind = record.Black.Kind,
            BlackModel = record.Black.Model,
            Rated = true,
            Result = record.Result,
            Termination = record.Termination,
            StartFen = startFen,
            CurrentFen = board.ToFen(),
            PlyCount = record.Moves.Count,
            Pgn = Pgn.Write(tags, startFen, record.Moves.Select(m => m.San), record.Result),
            CreatedAt = record.PlayedAt,
            StartedAt = record.PlayedAt,
            FinishedAt = record.PlayedAt,
        };
        return (game, moves);
    }

    private static bool IsStandard(string fen) => fen == ChessBoard.StartingPosition().ToFen();

    private static ChessBoard FromFen(string fen)
    {
        var board = new ChessBoard();
        board.LoadFen(fen);
        return board;
    }
}
