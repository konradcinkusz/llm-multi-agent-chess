using System.Text;
using System.Text.Json;
using Chess.Api.Catalog;
using Chess.Api.Contracts;
using Chess.Api.Persistence;
using Chess.Api.Persistence.Entities;
using Chess.Engine;
using Microsoft.EntityFrameworkCore;

namespace Chess.Api.Games;

/// <summary>
/// The <c>/api/games</c> routes (03 §5): create (the runner plays it), archive, detail, live events, PGN and abort.
/// The archive and detail read the database; the live events come from <see cref="IGameEventStream"/>.
/// </summary>
public static class GameEndpoints
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;

    /// <summary>How long a live stream stays quiet before a <c>: ping</c> comment is sent (03 §5.1).</summary>
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);

    private const string LlmKind = "llm";
    private const string RandomKind = "random";

    private static readonly JsonSerializerOptions EventJson = new(JsonSerializerDefaults.Web);

    /// <summary>Maps the game routes relative to the <c>/api</c> group.</summary>
    public static IEndpointRouteBuilder MapGameEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/games", CreateGameAsync);
        endpoints.MapGet("/games", ListGamesAsync);
        endpoints.MapGet("/games/{id:guid}", GetGameAsync);
        endpoints.MapGet("/games/{id:guid}/events", StreamEventsAsync);
        endpoints.MapGet("/games/{id:guid}/pgn", GetPgnAsync);
        endpoints.MapDelete("/games/{id:guid}", AbortGameAsync);
        return endpoints;
    }

    private static async Task<IResult> CreateGameAsync(
        CreateGameRequest? request,
        IDbContextFactory<ArenaDbContext> factory,
        IGameRunner runner,
        ModelCatalog catalog,
        TimeProvider clock,
        CancellationToken ct)
    {
        if (request?.White is null || request.Black is null)
        {
            return Problem(StatusCodes.Status400BadRequest, "Invalid game", "Both white and black players are required.");
        }

        var problem = await ValidateSideAsync("white", request.White, catalog, ct);
        problem ??= await ValidateSideAsync("black", request.Black, catalog, ct);
        if (problem is not null)
        {
            return problem;
        }

        var id = Guid.NewGuid();
        var start = ChessBoard.StartingPosition().ToFen();
        var game = new GameEntity
        {
            Id = id,
            WhiteKind = request.White.Kind,
            WhiteModel = request.White.ModelId,
            BlackKind = request.Black.Kind,
            BlackModel = request.Black.ModelId,
            Rated = request.Rated,
            Result = "*",
            StartFen = start,
            CurrentFen = start,
            CreatedAt = clock.GetUtcNow(),
        };

        await using var db = await factory.CreateDbContextAsync(ct);
        db.Games.Add(game);
        await db.SaveChangesAsync(ct);
        await runner.EnqueueAsync(id, ct);

        return TypedResults.Created($"/api/games/{id}", ToSummary(game));
    }

    /// <returns>The problem to answer with, or null when the side can be played.</returns>
    private static async Task<IResult?> ValidateSideAsync(string side, PlayerSpec player, ModelCatalog catalog, CancellationToken ct)
    {
        switch (player.Kind)
        {
            case RandomKind:
                return player.ModelId == ModelCatalog.RandomModelId
                    ? null
                    : Problem(StatusCodes.Status400BadRequest, "Invalid game",
                        $"The {side} random player's model must be '{ModelCatalog.RandomModelId}'.");

            case LlmKind:
                var listing = await catalog.GetModelsAsync(ct);
                if (listing.Warning is not null)
                {
                    return Problem(StatusCodes.Status503ServiceUnavailable, "LLM gateway not ready",
                        $"{listing.Warning}. Run `copilot` and log in, then try again.");
                }

                var model = listing.Models.FirstOrDefault(m => m.Id == player.ModelId);
                if (model is null || player.ModelId == ModelCatalog.RandomModelId)
                {
                    return Problem(StatusCodes.Status400BadRequest, "Invalid game",
                        $"The {side} player's model '{player.ModelId}' is not one the gateway offers.");
                }

                return model.Enabled
                    ? null
                    : Problem(StatusCodes.Status400BadRequest, "Invalid game",
                        $"The {side} player's model '{player.ModelId}' is not enabled.");

            default:
                return Problem(StatusCodes.Status400BadRequest, "Invalid game",
                    $"The {side} player's kind must be '{LlmKind}' or '{RandomKind}'.");
        }
    }

    private static async Task<IResult> ListGamesAsync(
        IDbContextFactory<ArenaDbContext> factory,
        string? model,
        string? status,
        int page = 1,
        int pageSize = DefaultPageSize,
        CancellationToken ct = default)
    {
        if (page < 1)
        {
            return Problem(StatusCodes.Status400BadRequest, "Invalid paging", "page must be 1 or greater.");
        }

        if (pageSize < 1 || pageSize > MaxPageSize)
        {
            return Problem(StatusCodes.Status400BadRequest, "Invalid paging",
                $"pageSize must be between 1 and {MaxPageSize}.");
        }

        if (status is not null and not ("queued" or "running" or "finished"))
        {
            return Problem(StatusCodes.Status400BadRequest, "Invalid filter", "status must be queued, running or finished.");
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        IQueryable<GameEntity> games = db.Games.AsNoTracking();

        if (model is not null)
        {
            games = games.Where(g => g.WhiteModel == model || g.BlackModel == model);
        }

        games = status switch
        {
            "queued" => games.Where(g => g.StartedAt == null && g.FinishedAt == null),
            "running" => games.Where(g => g.StartedAt != null && g.FinishedAt == null),
            "finished" => games.Where(g => g.FinishedAt != null),
            _ => games,
        };

        var total = await games.CountAsync(ct);

        // (page - 1) * pageSize can exceed int.MaxValue for a large page number; past the total there is nothing to read.
        var skip = (long)(page - 1) * pageSize;
        IReadOnlyList<GameSummaryDto> items = skip >= total
            ? []
            : (await games
                .OrderByDescending(g => g.CreatedAt)
                .ThenByDescending(g => g.Id)
                .Skip((int)skip)
                .Take(pageSize)
                .ToListAsync(ct))
                .Select(ToSummary)
                .ToList();

        return TypedResults.Ok(new PageDto<GameSummaryDto>(items, page, pageSize, total));
    }

    private static async Task<IResult> GetGameAsync(Guid id, IDbContextFactory<ArenaDbContext> factory, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var game = await db.Games.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id, ct);
        if (game is null)
        {
            return GameNotFound(id);
        }

        var moves = await db.Moves.AsNoTracking()
            .Where(m => m.GameId == id)
            .OrderBy(m => m.Ply)
            .ToListAsync(ct);

        return TypedResults.Ok(new GameDetailDto(
            ToSummary(game), game.StartFen, game.CurrentFen, moves.Select(ToMove).ToList()));
    }

    /// <summary>
    /// Streams the game's events as they are published, ending after its <see cref="GameFinished"/>. The subscription
    /// is taken before the game is read, so an event published in between is not lost; a game that has already
    /// finished gets its <see cref="GameFinished"/> from the database alone.
    /// </summary>
    private static async Task StreamEventsAsync(Guid id, IGameEventStream events, IDbContextFactory<ArenaDbContext> factory, HttpContext http)
    {
        var ct = http.RequestAborted;
        using var subscription = CancellationTokenSource.CreateLinkedTokenSource(ct);
        try
        {
            var live = events.SubscribeAsync(id, subscription.Token);

            await using var db = await factory.CreateDbContextAsync(ct);
            var game = await db.Games.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id, ct);
            if (game is null)
            {
                await GameNotFound(id).ExecuteAsync(http);
                return;
            }

            var response = http.Response;
            response.ContentType = "text/event-stream";
            response.Headers.CacheControl = "no-cache";
            response.Headers["X-Accel-Buffering"] = "no";
            await response.StartAsync(ct);

            if (game.FinishedAt is { } finishedAt)
            {
                await WriteEventAsync(response, new GameFinished(id, finishedAt, game.Result, game.Termination ?? string.Empty), ct);
                return;
            }

            await using var stream = live.GetAsyncEnumerator(subscription.Token);
            var next = stream.MoveNextAsync().AsTask();
            while (true)
            {
                var heartbeat = Task.Delay(HeartbeatInterval, ct);
                Task woke = await Task.WhenAny(next, heartbeat);
                if (!ReferenceEquals(woke, next))
                {
                    await response.WriteAsync(": ping\n\n", ct);
                    await response.Body.FlushAsync(ct);
                    continue;
                }

                if (!await next)
                {
                    return;
                }

                var e = stream.Current;
                await WriteEventAsync(response, e, ct);
                if (e is GameFinished)
                {
                    return;
                }

                next = stream.MoveNextAsync().AsTask();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The client went away; there is nothing left to send.
        }
        finally
        {
            // Releases the subscription when the stream was never read to its end.
            subscription.Cancel();
        }
    }

    private static async Task<IResult> GetPgnAsync(Guid id, IDbContextFactory<ArenaDbContext> factory, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var game = await db.Games.AsNoTracking()
            .Where(g => g.Id == id)
            .Select(g => new { g.Pgn })
            .FirstOrDefaultAsync(ct);
        if (game is null)
        {
            return GameNotFound(id);
        }

        // The PGN is written when the game ends; a game still queued or running has none yet.
        if (game.Pgn is null)
        {
            return Problem(StatusCodes.Status409Conflict, "No PGN yet", "The game has not ended, so it has no PGN yet.");
        }

        return TypedResults.File(Encoding.UTF8.GetBytes(game.Pgn), "application/x-chess-pgn", $"game-{id}.pgn");
    }

    private static async Task<IResult> AbortGameAsync(Guid id, IDbContextFactory<ArenaDbContext> factory, IGameRunner runner, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        if (!await db.Games.AnyAsync(g => g.Id == id, ct))
        {
            return GameNotFound(id);
        }

        // A game that is neither queued nor running has nothing left to abort, so the call still succeeds.
        runner.TryAbort(id);
        return TypedResults.NoContent();
    }

    private static async Task WriteEventAsync(HttpResponse response, IGameEvent e, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(e, e.GetType(), EventJson);
        await response.WriteAsync($"event: {e.GetType().Name}\ndata: {json}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }

    private static GameSummaryDto ToSummary(GameEntity game) => new(
        game.Id,
        game.TournamentId,
        new PlayerSpec(game.WhiteKind, game.WhiteModel),
        new PlayerSpec(game.BlackKind, game.BlackModel),
        game.Rated,
        StateOf(game),
        game.Result,
        game.Termination,
        game.PlyCount,
        game.CreatedAt,
        game.FinishedAt);

    private static string StateOf(GameEntity game) =>
        game.FinishedAt is not null ? "finished" : game.StartedAt is not null ? "running" : "queued";

    private static MoveDto ToMove(MoveEntity move) => new(
        move.Ply,
        move.Color,
        move.San,
        move.FenAfter,
        move.Attempts,
        move.LatencyMs,
        move.InputTokens,
        move.OutputTokens,
        move.CentipawnLoss,
        move.BestSan);

    private static IResult GameNotFound(Guid id) =>
        Problem(StatusCodes.Status404NotFound, "Game not found", $"No game has the id {id}.");

    private static IResult Problem(int status, string title, string detail) =>
        TypedResults.Problem(statusCode: status, title: title, detail: detail);
}
