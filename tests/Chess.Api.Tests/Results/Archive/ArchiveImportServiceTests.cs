using Chess.Api.Persistence;
using Chess.Api.Results;
using Chess.Api.Results.Archive;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Chess.Api.Tests.Results.Archive;

/// <summary>
/// The start-up import of the archive folder (the CI job that builds the public site waits for its state <c>done</c>): the
/// rating snapshots are written before that state, so the rating history is not empty when the site is exported.
/// </summary>
public class ArchiveImportServiceTests : IDisposable
{
    private static readonly DateTimeOffset Played = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "chess-import-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    // Fool's mate: 1.f3 e5 2.g4 Qh4#, Black wins.
    private static GameRecord FoolsMate(int n, string white, string black) => new(
        GameRecord.CurrentVersion, Guid.Parse($"00000000-0000-0000-0000-{n:000000000000}"),
        new RecordSide("llm", white), new RecordSide("llm", black),
        "0-1", "checkmate", null, Played.AddMinutes(n),
        [new RecordMove("f3"), new RecordMove("e5"), new RecordMove("g4"), new RecordMove("Qh4#")]);

    private void WriteArchive(params GameRecord[] records)
    {
        foreach (var record in records)
        {
            var file = Path.Combine(_root, GameRecordJson.RelativePath(record));
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, GameRecordJson.Serialize(record));
        }
    }

    private static IDbContextFactory<ArenaDbContext> Factory()
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<ArenaDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        return services.BuildServiceProvider().GetRequiredService<IDbContextFactory<ArenaDbContext>>();
    }

    private ArchiveImportService Service(IDbContextFactory<ArenaDbContext> factory, ArchiveImportStatus status)
    {
        var readiness = new DatabaseReadiness();
        readiness.MarkReady(0);
        return new ArchiveImportService(Options.Create(new ArchiveOptions { ImportFolder = _root }), readiness,
            new ResultsArchive(factory), factory, TimeProvider.System, status, NullLogger<ArchiveImportService>.Instance);
    }

    [Fact]
    public async Task The_status_is_done_only_after_the_rating_snapshots_of_the_imported_games_are_written()
    {
        WriteArchive(FoolsMate(1, "model-a", "model-b"), FoolsMate(2, "model-b", "model-a"), FoolsMate(3, "model-a", "model-b"));
        var factory = Factory();
        var status = new ArchiveImportStatus();
        var service = Service(factory, status);

        await service.StartAsync(CancellationToken.None);
        await service.ExecuteTask!;

        Assert.Equal("done", status.State);
        Assert.Equal(3, status.Added);
        Assert.Equal(3, status.SnapshotBatches);
        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(3 * 2, await db.RatingSnapshots.CountAsync()); // three batches of two models
        Assert.Equal(3, await db.RatingSnapshots.Select(s => s.AfterGameId).Distinct().CountAsync());
        var history = await RatingHistory.ReadAsync(db, "model-a", CancellationToken.None);
        Assert.Equal(3, history.Count);
    }

    [Fact]
    public async Task A_second_import_of_the_same_folder_writes_no_batch_twice()
    {
        WriteArchive(FoolsMate(1, "model-a", "model-b"), FoolsMate(2, "model-b", "model-a"));
        var factory = Factory();
        var first = Service(factory, new ArchiveImportStatus());
        await first.StartAsync(CancellationToken.None);
        await first.ExecuteTask!;

        var status = new ArchiveImportStatus();
        var second = Service(factory, status);
        await second.StartAsync(CancellationToken.None);
        await second.ExecuteTask!;

        Assert.Equal(("done", 0, 0), (status.State, status.Added, status.SnapshotBatches));
        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(2 * 2, await db.RatingSnapshots.CountAsync());
    }

    [Fact]
    public async Task The_snapshot_service_and_the_import_never_write_the_same_batch_twice()
    {
        WriteArchive(FoolsMate(1, "model-a", "model-b"), FoolsMate(2, "model-b", "model-a"), FoolsMate(3, "model-a", "model-b"));
        var factory = Factory();
        await new ResultsArchive(factory).ImportAsync(GameRecordFolder.Read(_root).Records, CancellationToken.None);

        var written = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            await using var db = await factory.CreateDbContextAsync();
            return await RatingSnapshotWriter.WriteNewAsync(db, TimeProvider.System, CancellationToken.None);
        }));

        Assert.Equal(3, written.Sum());
        await using var check = await factory.CreateDbContextAsync();
        Assert.Equal(3 * 2, await check.RatingSnapshots.CountAsync());
    }

    [Fact]
    public async Task Without_an_import_folder_the_service_does_nothing_and_the_status_stays_off()
    {
        var status = new ArchiveImportStatus();
        var factory = Factory();
        var readiness = new DatabaseReadiness();
        var service = new ArchiveImportService(Options.Create(new ArchiveOptions()), readiness, new ResultsArchive(factory), factory,
            TimeProvider.System, status, NullLogger<ArchiveImportService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await service.ExecuteTask!;

        Assert.Equal("off", status.State);
    }
}
