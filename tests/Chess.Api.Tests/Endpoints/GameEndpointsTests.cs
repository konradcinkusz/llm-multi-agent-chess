using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Chess.Api.Contracts;
using Chess.Api.Games;
using Chess.Api.Persistence;
using Chess.Api.Persistence.Entities;
using Chess.Api.Players;
using Chess.Api.Tests.Catalog;
using Chess.Engine;
using Chess.Llm;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Chess.Api.Tests.Endpoints;

/// <summary>The app on the InMemory provider with a named store, so that a test can seed it directly.</summary>
internal sealed class SeededApp(string databaseName) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Database:Provider", "InMemory");
        builder.UseSetting("Database:InMemoryName", databaseName);
        builder.UseSetting("Llm:Provider", "Fake");
    }

    public ArenaDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ArenaDbContext>().UseInMemoryDatabase(databaseName).Options);
}

/// <summary>
/// The app whose games wait, before their first ply, until <see cref="Open"/> is called. Each side's player is only
/// created then, so no event is published before the test has subscribed to the live stream.
/// </summary>
internal sealed class GatedApp : WebApplicationFactory<Program>
{
    private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Database:Provider", "InMemory");
        builder.UseSetting("Database:InMemoryName", Guid.NewGuid().ToString());
        builder.UseSetting("Llm:Provider", "Fake");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IChessPlayerFactory>();
            services.AddSingleton<IChessPlayerFactory>(new GatedPlayerFactory(_gate.Task));
        });
    }

    public void Open() => _gate.TrySetResult();
}

/// <summary>Random players that are only created once the gate is open.</summary>
internal sealed class GatedPlayerFactory(Task gate) : IChessPlayerFactory
{
    public async Task<IChessPlayer> CreateAsync(PlayerDescriptor descriptor, Guid gameId, Side side, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        return new RandomChessPlayer(descriptor, Random.Shared.Next());
    }
}

public class GameEndpointsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly PlayerSpec RandomPlayer = new("random", "random");
    private static readonly PlayerSpec FakeA = new("llm", "fake-a");

    private static CancellationTokenSource Deadline() => new(TimeSpan.FromSeconds(60));

    private static GameEntity Game(Guid id, string white = "fake-a", string black = "fake-b",
        DateTimeOffset? createdAt = null, DateTimeOffset? startedAt = null, DateTimeOffset? finishedAt = null,
        string result = "*", string? termination = null, string? pgn = null) => new()
    {
        Id = id, WhiteKind = "llm", WhiteModel = white, BlackKind = "llm", BlackModel = black,
        Rated = true, Result = result, Termination = termination, Pgn = pgn,
        StartFen = ChessBoard.StartingPosition().ToFen(), CurrentFen = ChessBoard.StartingPosition().ToFen(),
        CreatedAt = createdAt ?? T0, StartedAt = startedAt, FinishedAt = finishedAt,
    };

    private static async Task SeedAsync(SeededApp app, IEnumerable<GameEntity> games, IEnumerable<MoveEntity> moves)
    {
        await using var db = app.NewContext();
        db.Games.AddRange(games);
        db.Moves.AddRange(moves);
        await db.SaveChangesAsync();
    }

    private static async Task<GameSummaryDto> CreateAsync(HttpClient client, PlayerSpec white, PlayerSpec black)
    {
        var response = await client.PostAsJsonAsync("/api/games", new CreateGameRequest(white, black, Rated: true));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<GameSummaryDto>(Json))!;
    }

    private static async Task<JsonElement> ProblemAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Json);
    }

    /// <summary>Requests the live stream and returns once its headers have arrived, so the test can act next.</summary>
    private static Task<HttpResponseMessage> OpenStreamAsync(HttpClient client, Guid gameId, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/games/{gameId}/events");
        return client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
    }

    private static async Task<List<string>> ReadLinesToEndAsync(HttpResponseMessage response, CancellationToken ct)
    {
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(ct));
        var lines = new List<string>();
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            lines.Add(line);
        }

        return lines;
    }

    private sealed record Frame(string Name, JsonElement Data);

    private static List<Frame> Frames(IEnumerable<string> lines)
    {
        var frames = new List<Frame>();
        string? name = null;
        foreach (var line in lines)
        {
            if (line.StartsWith("event: ", StringComparison.Ordinal))
            {
                name = line["event: ".Length..];
            }
            else if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                frames.Add(new Frame(name!, JsonDocument.Parse(line["data: ".Length..]).RootElement.Clone()));
                name = null;
            }
        }

        return frames;
    }

    [Fact]
    public async Task Create_queues_a_random_game_and_returns_it_with_a_location()
    {
        using var app = new GatedApp();
        var client = app.CreateClient();

        var response = await client.PostAsJsonAsync("/api/games", new CreateGameRequest(RandomPlayer, RandomPlayer, Rated: true));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var summary = (await response.Content.ReadFromJsonAsync<GameSummaryDto>(Json))!;
        Assert.Equal($"/api/games/{summary.Id}", response.Headers.Location?.ToString());
        Assert.Equal(RandomPlayer, summary.White);
        Assert.Equal(RandomPlayer, summary.Black);
        Assert.True(summary.Rated);
        // The gate holds both players back, so the game has not started: it is queued, with no plies.
        Assert.Equal("queued", summary.State);
        Assert.Equal("*", summary.Result);
        Assert.Equal(0, summary.PlyCount);

        var detail = await client.GetFromJsonAsync<GameDetailDto>($"/api/games/{summary.Id}", Json);
        Assert.Equal(ChessBoard.StartingPosition().ToFen(), detail!.StartFen);
        Assert.Equal(detail.StartFen, detail.CurrentFen);
        Assert.Empty(detail.Moves);
    }

    [Fact]
    public async Task Create_accepts_an_llm_player_on_a_model_the_gateway_offers()
    {
        using var app = new GatedApp();

        var summary = await CreateAsync(app.CreateClient(), FakeA, RandomPlayer);

        Assert.Equal(FakeA, summary.White);
    }

    [Fact]
    public async Task Create_rejects_a_player_kind_that_is_neither_llm_nor_random()
    {
        using var app = new GatedApp();

        var response = await app.CreateClient().PostAsJsonAsync("/api/games",
            new CreateGameRequest(new PlayerSpec("human", "random"), RandomPlayer, Rated: true));

        var problem = await ProblemAsync(response, HttpStatusCode.BadRequest);
        Assert.Contains("kind", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Create_rejects_an_llm_model_the_gateway_does_not_offer()
    {
        using var app = new GatedApp();

        var response = await app.CreateClient().PostAsJsonAsync("/api/games",
            new CreateGameRequest(new PlayerSpec("llm", "gpt-nope"), RandomPlayer, Rated: true));

        var problem = await ProblemAsync(response, HttpStatusCode.BadRequest);
        Assert.Contains("gpt-nope", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Create_rejects_an_llm_model_the_gateway_lists_as_disabled()
    {
        using var app = new DisabledModelApp();

        var response = await app.CreateClient().PostAsJsonAsync("/api/games",
            new CreateGameRequest(new PlayerSpec("llm", "off"), RandomPlayer, Rated: true));

        var problem = await ProblemAsync(response, HttpStatusCode.BadRequest);
        Assert.Contains("not enabled", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Create_rejects_a_random_player_on_any_model_but_random()
    {
        using var app = new GatedApp();

        var response = await app.CreateClient().PostAsJsonAsync("/api/games",
            new CreateGameRequest(new PlayerSpec("random", "fake-a"), RandomPlayer, Rated: true));

        await ProblemAsync(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_rejects_an_llm_player_on_the_random_model()
    {
        using var app = new GatedApp();

        var response = await app.CreateClient().PostAsJsonAsync("/api/games",
            new CreateGameRequest(new PlayerSpec("llm", "random"), RandomPlayer, Rated: true));

        await ProblemAsync(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_rejects_a_request_without_both_players()
    {
        using var app = new GatedApp();

        var response = await app.CreateClient().PostAsJsonAsync("/api/games", new { white = RandomPlayer, rated = true });

        await ProblemAsync(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_with_an_llm_side_answers_503_telling_the_user_to_log_in_when_the_gateway_is_not_signed_in()
    {
        using var app = new NotAuthenticatedApp();

        var response = await app.CreateClient().PostAsJsonAsync("/api/games",
            new CreateGameRequest(FakeA, RandomPlayer, Rated: true));

        var problem = await ProblemAsync(response, HttpStatusCode.ServiceUnavailable);
        Assert.Contains("`copilot` and log in", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Create_still_accepts_a_random_game_when_the_gateway_is_not_signed_in()
    {
        using var app = new NotAuthenticatedApp();

        var summary = await CreateAsync(app.CreateClient(), RandomPlayer, RandomPlayer);

        Assert.Equal(RandomPlayer, summary.Black);
    }

    [Fact]
    public async Task Archive_lists_newest_first_and_echoes_the_paging_metadata()
    {
        using var app = new SeededApp(Guid.NewGuid().ToString());
        var client = app.CreateClient();
        await SeedAsync(app,
        [
            Game(Guid.NewGuid(), createdAt: T0.AddMinutes(1)),
            Game(Guid.NewGuid(), createdAt: T0.AddMinutes(2)),
            Game(Guid.NewGuid(), createdAt: T0.AddMinutes(3)),
        ], []);

        var first = await client.GetFromJsonAsync<PageDto<GameSummaryDto>>("/api/games?page=1&pageSize=2", Json);
        var last = await client.GetFromJsonAsync<PageDto<GameSummaryDto>>("/api/games?page=2&pageSize=2", Json);

        Assert.Equal(3, first!.Total);
        Assert.Equal(1, first.Page);
        Assert.Equal(2, first.PageSize);
        Assert.Equal([T0.AddMinutes(3), T0.AddMinutes(2)], first.Items.Select(g => g.CreatedAt));
        Assert.Equal([T0.AddMinutes(1)], last!.Items.Select(g => g.CreatedAt));
    }

    [Fact]
    public async Task Archive_filters_by_a_model_on_either_side()
    {
        using var app = new SeededApp(Guid.NewGuid().ToString());
        var client = app.CreateClient();
        await SeedAsync(app,
        [
            Game(Guid.NewGuid(), white: "fake-a", black: "fake-b"),
            Game(Guid.NewGuid(), white: "fake-b", black: "fake-c"),
            Game(Guid.NewGuid(), white: "fake-c", black: "fake-d"),
        ], []);

        var page = await client.GetFromJsonAsync<PageDto<GameSummaryDto>>("/api/games?model=fake-b", Json);

        Assert.Equal(2, page!.Total);
        Assert.All(page.Items, g => Assert.Contains("fake-b", new[] { g.White.ModelId, g.Black.ModelId }));
    }

    [Fact]
    public async Task Archive_filters_by_state_derived_from_the_game_timestamps()
    {
        using var app = new SeededApp(Guid.NewGuid().ToString());
        var client = app.CreateClient();
        await client.GetAsync("/api/games");
        var queued = Guid.NewGuid();
        var running = Guid.NewGuid();
        var finished = Guid.NewGuid();
        // Seeded after the host has started, so the startup sweep does not close the running game.
        await SeedAsync(app,
        [
            Game(queued),
            Game(running, startedAt: T0),
            Game(finished, startedAt: T0, finishedAt: T0.AddMinutes(5), result: "1-0", termination: "checkmate"),
        ], []);

        var runningPage = await client.GetFromJsonAsync<PageDto<GameSummaryDto>>("/api/games?status=running", Json);
        var finishedPage = await client.GetFromJsonAsync<PageDto<GameSummaryDto>>("/api/games?status=finished", Json);
        var queuedPage = await client.GetFromJsonAsync<PageDto<GameSummaryDto>>("/api/games?status=queued", Json);

        var onlyRunning = Assert.Single(runningPage!.Items);
        Assert.Equal(running, onlyRunning.Id);
        Assert.Equal("running", onlyRunning.State);
        var onlyFinished = Assert.Single(finishedPage!.Items);
        Assert.Equal(finished, onlyFinished.Id);
        Assert.Equal("finished", onlyFinished.State);
        Assert.Equal("checkmate", onlyFinished.Termination);
        var onlyQueued = Assert.Single(queuedPage!.Items);
        Assert.Equal(queued, onlyQueued.Id);
        Assert.Equal("queued", onlyQueued.State);
    }

    [Fact]
    public async Task Archive_rejects_an_unknown_status_and_bad_paging()
    {
        using var app = new SeededApp(Guid.NewGuid().ToString());
        var client = app.CreateClient();

        await ProblemAsync(await client.GetAsync("/api/games?status=paused"), HttpStatusCode.BadRequest);
        await ProblemAsync(await client.GetAsync("/api/games?page=0"), HttpStatusCode.BadRequest);
        await ProblemAsync(await client.GetAsync("/api/games?pageSize=500"), HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Detail_returns_the_summary_fens_and_moves_in_ply_order_with_their_optional_fields()
    {
        using var app = new SeededApp(Guid.NewGuid().ToString());
        var client = app.CreateClient();
        var game = Guid.NewGuid();
        await SeedAsync(app,
            [Game(game, startedAt: T0, finishedAt: T0.AddMinutes(9), result: "*", termination: "adjudicated")],
            [
                new MoveEntity { GameId = game, Ply = 2, Color = "black", San = "e5", FenAfter = "fen2", Attempts = 1, LatencyMs = 20, PlayedAt = T0 },
                new MoveEntity { GameId = game, Ply = 1, Color = "white", San = "e4", FenAfter = "fen1", Attempts = 2, LatencyMs = 40,
                    InputTokens = 100, OutputTokens = 7, CentipawnLoss = 12, BestSan = "Nf3", PlayedAt = T0 },
            ]);

        var detail = await client.GetFromJsonAsync<GameDetailDto>($"/api/games/{game}", Json);

        Assert.Equal(game, detail!.Summary.Id);
        Assert.Equal("finished", detail.Summary.State);
        Assert.Equal("adjudicated", detail.Summary.Termination);
        Assert.Equal([1, 2], detail.Moves.Select(m => m.Ply));
        var white = detail.Moves[0];
        Assert.Equal("e4", white.San);
        Assert.Equal("white", white.Color);
        Assert.Equal(2, white.Attempts);
        Assert.Equal(100, white.InputTokens);
        Assert.Equal(12, white.CentipawnLoss);
        Assert.Equal("Nf3", white.BestSan);
        var black = detail.Moves[1];
        Assert.Null(black.InputTokens);
        Assert.Null(black.CentipawnLoss);
        Assert.Null(black.BestSan);
    }

    [Fact]
    public async Task Detail_of_an_unknown_game_is_404()
    {
        using var app = new SeededApp(Guid.NewGuid().ToString());

        var response = await app.CreateClient().GetAsync($"/api/games/{Guid.NewGuid()}");

        await ProblemAsync(response, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Pgn_of_a_finished_game_downloads_as_chess_pgn_with_a_filename()
    {
        using var app = new SeededApp(Guid.NewGuid().ToString());
        var client = app.CreateClient();
        var game = Guid.NewGuid();
        const string pgn = "[Event \"LLM Chess Arena\"]\n\n1. e4 e5 0-1";
        await SeedAsync(app, [Game(game, startedAt: T0, finishedAt: T0.AddMinutes(3), result: "0-1", termination: "checkmate", pgn: pgn)], []);

        var response = await client.GetAsync($"/api/games/{game}/pgn");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/x-chess-pgn", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal($"game-{game}.pgn", response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        Assert.Equal(pgn, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Pgn_of_a_game_that_has_not_ended_is_409()
    {
        using var app = new SeededApp(Guid.NewGuid().ToString());
        var client = app.CreateClient();
        var game = Guid.NewGuid();
        await SeedAsync(app, [Game(game, startedAt: T0)], []);

        await ProblemAsync(await client.GetAsync($"/api/games/{game}/pgn"), HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Pgn_of_an_unknown_game_is_404()
    {
        using var app = new SeededApp(Guid.NewGuid().ToString());

        await ProblemAsync(await app.CreateClient().GetAsync($"/api/games/{Guid.NewGuid()}/pgn"), HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Events_of_a_finished_game_are_one_game_finished_and_the_stream_closes()
    {
        using var app = new SeededApp(Guid.NewGuid().ToString());
        var client = app.CreateClient();
        var game = Guid.NewGuid();
        var finishedAt = T0.AddMinutes(4);
        await SeedAsync(app, [Game(game, startedAt: T0, finishedAt: finishedAt, result: "0-1", termination: "forfeit-black")], []);

        using var deadline = Deadline();
        using var response = await OpenStreamAsync(client, game, deadline.Token);
        var frames = Frames(await ReadLinesToEndAsync(response, deadline.Token));

        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        var only = Assert.Single(frames);
        Assert.Equal("GameFinished", only.Name);
        Assert.Equal(game, only.Data.GetProperty("gameId").GetGuid());
        Assert.Equal("0-1", only.Data.GetProperty("result").GetString());
        Assert.Equal("forfeit-black", only.Data.GetProperty("termination").GetString());
        Assert.Equal(finishedAt, only.Data.GetProperty("at").GetDateTimeOffset());
    }

    [Fact]
    public async Task Events_of_an_unknown_game_are_404()
    {
        using var app = new SeededApp(Guid.NewGuid().ToString());

        using var deadline = Deadline();
        using var response = await OpenStreamAsync(app.CreateClient(), Guid.NewGuid(), deadline.Token);

        await ProblemAsync(response, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Live_stream_carries_every_ply_in_order_and_closes_after_game_finished()
    {
        using var app = new GatedApp();
        var client = app.CreateClient();
        using var deadline = Deadline();
        var summary = await CreateAsync(client, RandomPlayer, RandomPlayer);

        using var response = await OpenStreamAsync(client, summary.Id, deadline.Token);
        app.Open();
        var frames = Frames(await ReadLinesToEndAsync(response, deadline.Token));

        var detail = await client.GetFromJsonAsync<GameDetailDto>($"/api/games/{summary.Id}", Json);
        Assert.Equal("finished", detail!.Summary.State);
        Assert.Equal(detail.Summary.PlyCount, detail.Moves.Count);

        // Each ply is announced, then played, before the next one; nothing comes after the game ends.
        var names = frames.Select(f => f.Name).ToList();
        Assert.Equal("GameFinished", names[^1]);
        var plies = frames.Where(f => f.Name is "MoveStarted" or "MovePlayed")
            .Select(f => (f.Name, Ply: f.Data.GetProperty("ply").GetInt32()))
            .ToList();
        var expected = Enumerable.Range(1, detail.Summary.PlyCount)
            .SelectMany(ply => new[] { ("MoveStarted", ply), ("MovePlayed", ply) })
            .ToList();
        Assert.Equal(expected, plies);
        Assert.Equal(frames.Count(f => f.Name == "MovePlayed"), detail.Summary.PlyCount);

        var end = frames[^1].Data;
        Assert.Equal(detail.Summary.Result, end.GetProperty("result").GetString());
        Assert.Equal(detail.Summary.Termination, end.GetProperty("termination").GetString());
    }

    [Fact]
    public async Task Live_stream_sends_a_ping_comment_each_heartbeat_while_the_game_is_quiet()
    {
        using var app = new GatedApp();
        var client = app.CreateClient();
        using var deadline = Deadline();
        var summary = await CreateAsync(client, RandomPlayer, RandomPlayer);

        using var response = await OpenStreamAsync(client, summary.Id, deadline.Token);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(deadline.Token));
        string? line;
        do
        {
            line = await reader.ReadLineAsync(deadline.Token);
        }
        while (line is not null and not ": ping");

        // The game was still waiting at the gate when the ping arrived, so it had no events of its own yet.
        Assert.Equal(": ping", line);
        app.Open();
        var rest = new List<string>();
        while (await reader.ReadLineAsync(deadline.Token) is { } more)
        {
            rest.Add(more);
        }

        Assert.Equal("GameFinished", Frames(rest)[^1].Name);
    }

    [Fact]
    public async Task Delete_of_an_unknown_game_is_404()
    {
        using var app = new GatedApp();

        var response = await app.CreateClient().DeleteAsync($"/api/games/{Guid.NewGuid()}");

        await ProblemAsync(response, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_aborts_a_running_game_which_then_ends_as_aborted()
    {
        using var app = new GatedApp();
        var client = app.CreateClient();
        using var deadline = Deadline();
        var summary = await CreateAsync(client, RandomPlayer, RandomPlayer);
        using var response = await OpenStreamAsync(client, summary.Id, deadline.Token);

        var delete = await client.DeleteAsync($"/api/games/{summary.Id}", deadline.Token);

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        var finished = Assert.Single(Frames(await ReadLinesToEndAsync(response, deadline.Token)));
        Assert.Equal("GameFinished", finished.Name);
        Assert.Equal("aborted", finished.Data.GetProperty("termination").GetString());
        var detail = await client.GetFromJsonAsync<GameDetailDto>($"/api/games/{summary.Id}", Json, deadline.Token);
        Assert.Equal("aborted", detail!.Summary.Termination);
        Assert.Equal("finished", detail.Summary.State);
    }

    /// <summary>An app whose gateway lists one model that is switched off.</summary>
    private sealed class DisabledModelApp : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Database:Provider", "InMemory");
            builder.UseSetting("Llm:Provider", "Fake");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ILlmGateway>();
                services.AddSingleton<ILlmGateway>(new StubGateway(
                    new LlmGatewayStatus(LlmGatewayState.Ready, "Copilot ready"),
                    [new LlmModelInfo("off", "Off", 0, Enabled: false)]));
            });
        }
    }
}
