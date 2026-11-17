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

    [Theory]
    [InlineData("gpt-5.1")]
    [InlineData("claude-sonnet-4.5@high~think")]
    [InlineData("org/Model_7B:q4+x")]
    public void A_model_id_of_ascii_letters_digits_and_the_listed_marks_is_valid(string model)
    {
        var record = FoolsMate() with { White = new RecordSide("llm", model) };

        Assert.Empty(GameRecordValidator.Validate(record));
    }

    [Theory]
    [InlineData("model\"]\n[Result \"1-0\"")]   // a PGN tag injection
    [InlineData("model with space")]
    [InlineData("mod\u00e9l")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("model\n")]
    [InlineData("a--b")]
    [InlineData("")]
    public void A_model_id_outside_the_ascii_set_is_refused(string model)
    {
        var record = FoolsMate() with { Black = new RecordSide("llm", model) };

        Assert.Contains(GameRecordValidator.Validate(record), e => e.StartsWith("black.model", StringComparison.Ordinal));
    }

    [Fact]
    public void A_start_position_other_than_the_standard_one_is_refused()
    {
        const string standard = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
        const string mateInOne = "6k1/5ppp/8/8/8/8/5PPP/3R2K1 w - - 0 1";
        var record = FoolsMate();

        Assert.Empty(GameRecordValidator.Validate(record with { StartFen = standard }));
        Assert.Contains(GameRecordValidator.Validate(record with { StartFen = mateInOne, Moves = [new RecordMove("Rd8#")], Result = "1-0" }),
            e => e.Contains("standard starting position", StringComparison.Ordinal));
        Assert.NotEmpty(GameRecordValidator.Validate(record with { StartFen = "not a fen" }));
    }

    [Fact]
    public void A_game_played_in_the_future_is_refused_and_one_a_day_ahead_is_not()
    {
        var now = new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.Zero);

        Assert.Contains(GameRecordValidator.Validate(FoolsMate() with { PlayedAt = now.AddDays(3) }, now), e => e.Contains("future", StringComparison.Ordinal));
        Assert.Empty(GameRecordValidator.Validate(FoolsMate() with { PlayedAt = now.AddHours(20) }, now));
    }

    [Fact]
    public void Best_moves_and_token_counts_are_bounded()
    {
        var long_ = new RecordMove("f3", BestSan: new string('a', 40));
        var weird = new RecordMove("f3", BestSan: "<b>x</b>");
        var huge = new RecordMove("f3", InputTokens: GameRecordValidator.MaxTokens + 1);
        var fine = new RecordMove("f3", InputTokens: 120_000, OutputTokens: 4_000, BestSan: "O-O-O");
        var inPlay = FoolsMate() with { Termination = "adjudicated", Result = "1/2-1/2" };

        foreach (var move in new[] { long_, weird, huge })
        {
            Assert.NotEmpty(GameRecordValidator.Validate(inPlay with { Moves = [move] }));
        }

        Assert.Empty(GameRecordValidator.Validate(inPlay with { Moves = [fine] }));
    }

    [Fact]
    public void An_empty_move_in_the_list_is_reported_instead_of_crashing()
    {
        var record = FoolsMate() with { Moves = [new RecordMove("f3"), null!] };

        Assert.Contains(GameRecordValidator.Validate(record), e => e.StartsWith("ply 2: the move is empty", StringComparison.Ordinal));
    }

    [Fact]
    public void A_runner_decided_result_is_not_checked_against_the_board_beyond_the_game_being_in_play()
    {
        // The replay cannot tell what Stockfish evaluated at the adjudication, so an adjudicated or budget game may carry any
        // result at any length. A maintainer reviews it (docs/RESULTS-ARCHIVE.md, "What review decides").
        foreach (var termination in new[] { "adjudicated", "budget" })
        {
            foreach (var result in new[] { "1-0", "0-1", "1/2-1/2" })
            {
                Assert.Empty(GameRecordValidator.Validate(FoolsMate() with
                {
                    Termination = termination, Result = result, Moves = [new RecordMove("e4"), new RecordMove("e5")],
                }));
            }
        }
    }

    [Fact]
    public void The_path_of_a_game_does_not_depend_on_the_current_culture()
    {
        var before = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("ar-SA");
            var record = FoolsMate(Guid.Parse("0b0f6a0e-0000-0000-0000-000000000001"));

            Assert.Equal("games/2026/11/0b0f6a0e-0000-0000-0000-000000000001.json", GameRecordJson.RelativePath(record));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = before;
        }
    }

    [Fact]
    public void Two_model_ids_that_would_share_one_site_page_are_refused_at_the_file_that_adds_the_second()
    {
        // The files are read in path order, which is id order, so the first id is the file that wins.
        static Guid Id(int n) => Guid.Parse($"00000000-0000-0000-0000-{n:000000000000}");
        var first = FoolsMate(Id(1)) with { White = new RecordSide("llm", "GPT-5"), Black = new RecordSide("llm", "model-b") };
        var same = FoolsMate(Id(2)) with { White = new RecordSide("llm", "GPT-5"), Black = new RecordSide("llm", "model-b") };
        var second = FoolsMate(Id(3)) with { White = new RecordSide("llm", "gpt-5"), Black = new RecordSide("llm", "model-b") };
        var dashed = FoolsMate(Id(4)) with { White = new RecordSide("llm", "model@b"), Black = new RecordSide("llm", "model-a") };
        Write(_root, first);
        Write(_root, same);
        Write(_root, second);
        Write(_root, dashed);

        var read = GameRecordFolder.Read(_root);

        Assert.Equal(2, read.Records.Count);
        Assert.Equal(2, read.Errors.Count);
        Assert.All(read.Errors, e => Assert.Contains("would share one page", e, StringComparison.Ordinal));
        Assert.Contains(read.Errors, e => e.Contains("'gpt-5'", StringComparison.Ordinal) && e.Contains("'GPT-5'", StringComparison.Ordinal));
        Assert.Contains(read.Errors, e => e.Contains("'model@b'", StringComparison.Ordinal));
    }

    [Fact]
    public void The_folder_is_read_and_every_problem_names_its_file()
    {
        var good = FoolsMate();
        var duplicate = FoolsMate(good.Id) with { PlayedAt = Played.AddDays(-40) };
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
