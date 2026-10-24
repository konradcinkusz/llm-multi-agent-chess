using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Chess.Api.Contracts;
using Chess.Api.Persistence;
using Chess.Api.Persistence.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Xunit;

namespace Chess.Api.Tests.Attempts;

public class AttemptsEndpointTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>A host on the InMemory provider whose store is named, so the test can seed it directly.</summary>
    private sealed class ArenaHost(string databaseName) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Database:Provider", "InMemory");
            builder.UseSetting("Database:InMemoryName", databaseName);
        }

        public ArenaDbContext NewContext() =>
            new(new DbContextOptionsBuilder<ArenaDbContext>().UseInMemoryDatabase(databaseName).Options);
    }

    private static ArenaHost NewHost() => new(Guid.NewGuid().ToString());

    private static GameEntity Game(Guid id, string white, string black) => new()
    {
        Id = id, WhiteKind = "llm", WhiteModel = white, BlackKind = "llm", BlackModel = black,
        Result = "*", StartFen = "start", CurrentFen = "current", CreatedAt = T0,
    };

    private static MoveAttemptEntity Attempt(Guid gameId, int ply, int number, DateTimeOffset at,
        bool legal = true) => new()
    {
        GameId = gameId, Ply = ply, Number = number, Prompt = $"prompt {ply}.{number}",
        RawReply = $"reply {ply}.{number}", ParsedSan = legal ? "e4" : null, Legal = legal,
        LatencyMs = 100 + ply, Error = legal ? null : "illegal", CreatedAt = at,
    };

    private static async Task SeedAsync(ArenaHost host, IEnumerable<GameEntity> games,
        IEnumerable<MoveAttemptEntity> attempts)
    {
        await using var db = host.NewContext();
        db.Games.AddRange(games);
        db.MoveAttempts.AddRange(attempts);
        await db.SaveChangesAsync();
    }

    private static async Task<PageDto<AttemptDto>> GetPageAsync(HttpClient client, string query)
    {
        var response = await client.GetAsync($"/api/attempts?{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PageDto<AttemptDto>>(Json))!;
    }

    [Fact]
    public async Task Lists_newest_first_and_echoes_the_paging_metadata()
    {
        using var host = NewHost();
        var game = Guid.NewGuid();
        await SeedAsync(host, [Game(game, "model-a", "model-b")],
            Enumerable.Range(1, 5).Select(ply => Attempt(game, ply, 1, T0.AddMinutes(ply))));
        var client = host.CreateClient();

        var first = await GetPageAsync(client, "page=1&pageSize=2");
        var last = await GetPageAsync(client, "page=3&pageSize=2");

        Assert.Equal(5, first.Total);
        Assert.Equal(1, first.Page);
        Assert.Equal(2, first.PageSize);
        Assert.Equal([5, 4], first.Items.Select(a => a.Ply));
        Assert.Equal([1], last.Items.Select(a => a.Ply));
        Assert.Equal(5, last.Total);
    }

    [Fact]
    public async Task Page_size_defaults_to_fifty()
    {
        using var host = NewHost();
        var game = Guid.NewGuid();
        await SeedAsync(host, [Game(game, "model-a", "model-b")],
            Enumerable.Range(1, 51).Select(ply => Attempt(game, ply, 1, T0.AddMinutes(ply))));
        var client = host.CreateClient();

        var page = await GetPageAsync(client, "");

        Assert.Equal(50, page.PageSize);
        Assert.Equal(50, page.Items.Count);
        Assert.Equal(51, page.Total);
        Assert.Equal(51, page.Items[0].Ply);
    }

    [Fact]
    public async Task Maps_every_field_and_takes_the_model_from_the_side_that_played_the_ply()
    {
        using var host = NewHost();
        var game = Guid.NewGuid();
        await SeedAsync(host, [Game(game, "model-a", "model-b")],
        [
            Attempt(game, 1, 1, T0),
            Attempt(game, 2, 1, T0.AddMinutes(1), legal: false),
        ]);
        var client = host.CreateClient();

        var page = await GetPageAsync(client, "");

        var black = Assert.Single(page.Items, a => a.Ply == 2);
        Assert.Equal(game, black.GameId);
        Assert.Equal(1, black.Number);
        Assert.Equal("model-b", black.ModelId);
        Assert.Equal("prompt 2.1", black.Prompt);
        Assert.Equal("reply 2.1", black.RawReply);
        Assert.Null(black.ParsedSan);
        Assert.False(black.Legal);
        Assert.Equal(102, black.LatencyMs);
        Assert.Equal("illegal", black.Error);
        Assert.Equal(T0.AddMinutes(1), black.CreatedAt);

        var white = Assert.Single(page.Items, a => a.Ply == 1);
        Assert.Equal("model-a", white.ModelId);
        Assert.Equal("e4", white.ParsedSan);
        Assert.True(white.Legal);
        Assert.Null(white.Error);
    }

    [Fact]
    public async Task Filters_by_game()
    {
        using var host = NewHost();
        var wanted = Guid.NewGuid();
        var other = Guid.NewGuid();
        await SeedAsync(host, [Game(wanted, "model-a", "model-b"), Game(other, "model-a", "model-b")],
        [
            Attempt(wanted, 1, 1, T0),
            Attempt(wanted, 2, 1, T0.AddMinutes(1)),
            Attempt(other, 1, 1, T0.AddMinutes(2)),
        ]);
        var client = host.CreateClient();

        var page = await GetPageAsync(client, $"gameId={wanted}");

        Assert.Equal(2, page.Total);
        Assert.All(page.Items, a => Assert.Equal(wanted, a.GameId));
    }

    [Fact]
    public async Task Filters_by_model_on_the_side_it_played()
    {
        using var host = NewHost();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        // model-a is White in `first` and Black in `second`; model-b the reverse.
        await SeedAsync(host, [Game(first, "model-a", "model-b"), Game(second, "model-b", "model-a")],
        [
            Attempt(first, 1, 1, T0),
            Attempt(first, 2, 1, T0.AddMinutes(1)),
            Attempt(second, 1, 1, T0.AddMinutes(2)),
            Attempt(second, 2, 1, T0.AddMinutes(3)),
        ]);
        var client = host.CreateClient();

        var page = await GetPageAsync(client, "model=model-a");

        Assert.Equal(2, page.Total);
        Assert.All(page.Items, a => Assert.Equal("model-a", a.ModelId));
        Assert.Equal(
            new[] { (second, 2), (first, 1) },
            page.Items.Select(a => (a.GameId, a.Ply)).ToArray());
    }

    [Fact]
    public async Task Filters_by_legal_and_leaves_all_attempts_when_absent()
    {
        using var host = NewHost();
        var game = Guid.NewGuid();
        await SeedAsync(host, [Game(game, "model-a", "model-b")],
        [
            Attempt(game, 1, 1, T0, legal: false),
            Attempt(game, 1, 2, T0.AddSeconds(1)),
            Attempt(game, 2, 1, T0.AddSeconds(2), legal: false),
        ]);
        var client = host.CreateClient();

        var illegal = await GetPageAsync(client, "legal=false");
        var legal = await GetPageAsync(client, "legal=true");
        var all = await GetPageAsync(client, "");

        Assert.Equal(2, illegal.Total);
        Assert.All(illegal.Items, a => Assert.False(a.Legal));
        Assert.Equal(1, legal.Total);
        Assert.All(legal.Items, a => Assert.True(a.Legal));
        Assert.Equal(3, all.Total);
    }

    [Fact]
    public async Task Combines_game_model_and_legal_filters()
    {
        using var host = NewHost();
        var target = Guid.NewGuid();
        var other = Guid.NewGuid();
        await SeedAsync(host, [Game(target, "model-a", "model-b"), Game(other, "model-a", "model-b")],
        [
            Attempt(target, 1, 1, T0, legal: false),
            Attempt(target, 1, 2, T0.AddSeconds(1)),
            Attempt(target, 2, 1, T0.AddSeconds(2), legal: false),
            Attempt(other, 1, 1, T0.AddSeconds(3), legal: false),
        ]);
        var client = host.CreateClient();

        var page = await GetPageAsync(client, $"gameId={target}&model=model-a&legal=false");

        var only = Assert.Single(page.Items);
        Assert.Equal(target, only.GameId);
        Assert.Equal(1, only.Ply);
        Assert.Equal(1, only.Number);
        Assert.Equal(1, page.Total);
    }

    [Fact]
    public async Task Page_past_the_end_has_no_items_but_the_total()
    {
        using var host = NewHost();
        var game = Guid.NewGuid();
        await SeedAsync(host, [Game(game, "model-a", "model-b")],
            Enumerable.Range(1, 3).Select(ply => Attempt(game, ply, 1, T0.AddMinutes(ply))));
        var client = host.CreateClient();

        var page = await GetPageAsync(client, "page=10&pageSize=200");

        Assert.Empty(page.Items);
        Assert.Equal(3, page.Total);
    }

    [Fact]
    public async Task A_huge_page_number_does_not_overflow_into_an_error()
    {
        using var host = NewHost();
        var game = Guid.NewGuid();
        await SeedAsync(host, [Game(game, "model-a", "model-b")],
            Enumerable.Range(1, 3).Select(ply => Attempt(game, ply, 1, T0.AddMinutes(ply))));
        var client = host.CreateClient();

        var page = await GetPageAsync(client, $"page={int.MaxValue}&pageSize=200");

        Assert.Empty(page.Items);
        Assert.Equal(3, page.Total);
    }

    [Fact]
    public async Task Empty_log_returns_an_empty_page()
    {
        using var host = NewHost();
        var client = host.CreateClient();

        var page = await GetPageAsync(client, "");

        Assert.Empty(page.Items);
        Assert.Equal(0, page.Total);
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("page=-1")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=201")]
    public async Task Rejects_out_of_range_paging_with_a_problem(string query)
    {
        using var host = NewHost();
        var client = host.CreateClient();

        var response = await client.GetAsync($"/api/attempts?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Accepts_the_maximum_page_size()
    {
        using var host = NewHost();
        var client = host.CreateClient();

        var page = await GetPageAsync(client, "pageSize=200");

        Assert.Equal(200, page.PageSize);
    }

    [Fact]
    public async Task Rejects_a_malformed_game_id()
    {
        using var host = NewHost();
        var client = host.CreateClient();

        var response = await client.GetAsync("/api/attempts?gameId=not-a-guid");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
