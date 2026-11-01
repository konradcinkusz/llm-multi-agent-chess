using Chess.Api.Persistence;
using Chess.Api.Ratings;
using Chess.Api.Results.Archive;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Chess.Api.Tests.Results.Archive;

public class ResultsArchiveTests : IDisposable
{
    private static readonly DateTimeOffset Played = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "chess-archive-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    // Fool's mate: 1.f3 e5 2.g4 Qh4#, Black wins.
    private static GameRecord FoolsMate(Guid? id = null) => new(
        GameRecord.CurrentVersion, id ?? Guid.NewGuid(),
        new RecordSide("llm", "model-a"), new RecordSide("llm", "model-b"),
        "0-1", "checkmate", null, Played,
        [new RecordMove("f3"), new RecordMove("e5", 2, 900, 120, 8), new RecordMove("g4"), new RecordMove("Qh4#", CentipawnLoss: 0, BestSan: "Qh4#")]);

    private static void Write(string root, GameRecord record, string? path = null)
    {
        var file = Path.Combine(root, path ?? GameRecordJson.RelativePath(record));
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, GameRecordJson.Serialize(record));
    }

    [Fact]
    public void A_real_game_is_valid()
    {
        Assert.Empty(GameRecordValidator.Validate(FoolsMate()));
    }

    [Fact]
    public void An_illegal_move_is_reported_with_its_ply()
    {
        var record = FoolsMate() with { Moves = [new RecordMove("f3"), new RecordMove("Ke5")] };
        Assert.Contains(GameRecordValidator.Validate(record), e => e.StartsWith("ply 2:", StringComparison.Ordinal));
    }

    [Fact]
    public void A_result_the_final_position_does_not_give_is_refused()
    {
        var record = FoolsMate() with { Result = "1-0" };
        Assert.NotEmpty(GameRecordValidator.Validate(record));
    }

    [Fact]
    public void A_move_after_the_mate_is_refused()
    {
        var moves = FoolsMate().Moves.Append(new RecordMove("a3")).ToList();
        Assert.Contains(GameRecordValidator.Validate(FoolsMate() with { Moves = moves }), e => e.Contains("already over", StringComparison.Ordinal));
    }

    [Fact]
    public void A_forfeit_needs_the_forfeiting_side_to_move()
    {
        var record = FoolsMate() with { Result = "0-1", Termination = "forfeit-white", Moves = [new RecordMove("e4")] };
        Assert.NotEmpty(GameRecordValidator.Validate(record)); // after 1.e4 Black is to move
        Assert.Empty(GameRecordValidator.Validate(record with { Result = "1-0", Termination = "forfeit-black" }));
    }

    [Fact]
    public void A_random_side_must_be_named_random_and_an_unknown_field_is_refused()
    {
        Assert.NotEmpty(GameRecordValidator.Validate(FoolsMate() with { White = new RecordSide("random", "gpt") }));
        Assert.Throws<System.Text.Json.JsonException>(() => GameRecordJson.Deserialize("""{"version":1,"extra":1}"""));
    }

    [Fact]
    public void The_folder_is_read_and_every_problem_names_its_file()
    {
        var good = FoolsMate();
        var duplicate = FoolsMate(good.Id) with { PlayedAt = Played.AddDays(40) };
        Write(_root, good);
        Write(_root, duplicate);
        Write(_root, FoolsMate(), "games/2026/11/misplaced.json");
        File.WriteAllText(Path.Combine(_root, "games", "2026", "11", ".gitkeep"), "");
        File.WriteAllText(Path.Combine(_root, "games", "2026", "11", "broken.json"), "{ nope");

        var read = GameRecordFolder.Read(_root);

        Assert.Single(read.Records);
        Assert.Contains(read.Errors, e => e.Contains("already in", StringComparison.Ordinal));
        Assert.Contains(read.Errors, e => e.StartsWith("games/2026/11/misplaced.json: the file belongs at", StringComparison.Ordinal));
        Assert.Contains(read.Errors, e => e.StartsWith("games/2026/11/broken.json: not a game file", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Import_then_export_gives_the_same_files_and_the_same_ratings()
    {
        var records = new[] { FoolsMate(), FoolsMate() with { Id = Guid.NewGuid(), White = new RecordSide("random", "random") } };
        var factory = Factory();
        var archive = new ResultsArchive(factory);

        var first = await archive.ImportAsync(records, CancellationToken.None);
        var again = await archive.ImportAsync(records, CancellationToken.None);
        Assert.Equal((2, 0), (first.Added, first.AlreadyThere));
        Assert.Equal((0, 2), (again.Added, again.AlreadyThere));

        var loaded = await RatedGameLoader.LoadAsync(factory, null, CancellationToken.None);
        Assert.Equal(2, loaded.Outcomes.Count);

        var exported = await archive.ExportAsync(_root, CancellationToken.None);
        Assert.Equal(2, exported.Written);
        Assert.Equal(0, (await archive.ExportAsync(_root, CancellationToken.None)).Written); // never rewrites a file

        var read = GameRecordFolder.Read(_root);
        Assert.Empty(read.Errors);
        Assert.Equal(records.Select(r => r.Id).Order(), read.Records.Select(r => r.Id).Order());
        Assert.Equal(records[0].Moves, read.Records.Single(r => r.Id == records[0].Id).Moves);
    }

    [Fact]
    public void The_results_folder_of_the_repository_is_valid()
    {
        // The check a pull request that adds games runs in CI: every file replays, sits at its path and is unique.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "global.json"))) dir = dir.Parent;
        Assert.NotNull(dir);

        var read = GameRecordFolder.Read(Path.Combine(dir.FullName, "results"));

        Assert.True(read.Errors.Count == 0, string.Join('\n', read.Errors));
    }

    private static IDbContextFactory<ArenaDbContext> Factory()
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<ArenaDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        return services.BuildServiceProvider().GetRequiredService<IDbContextFactory<ArenaDbContext>>();
    }
}
