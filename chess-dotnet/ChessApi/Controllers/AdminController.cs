using ChessApi.Data;
using ChessApi.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ChessApi.Controllers;

[ApiController]
[Route("api/admin")]
public class AdminController(IDbContextFactory<AppDbContext> dbFactory) : ControllerBase
{
    [HttpGet("stats")]
    public async Task<ActionResult<AdminStats>> GetStats()
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var totalGames = await db.Games.CountAsync();
        var totalPrompts = await db.PromptLogs.CountAsync();
        var totalMoves = await db.Moves.CountAsync();
        var activeGames = await db.Games.CountAsync(g => g.Status == "Ongoing" || g.Status == "Check!");
        var completedGames = await db.Games.CountAsync(g =>
            g.Status != "Ongoing" && g.Status != "Check!" && g.CompletedAt != null);

        var playerNames = await db.Games
            .Where(g => g.PlayerName != null)
            .Select(g => g.PlayerName!)
            .Distinct()
            .OrderBy(n => n)
            .ToListAsync();

        return Ok(new AdminStats(totalGames, totalPrompts, totalMoves,
            activeGames, completedGames, playerNames));
    }

    [HttpGet("players")]
    public async Task<ActionResult<List<string>>> GetPlayers()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var players = await db.Games
            .Where(g => g.PlayerName != null)
            .Select(g => g.PlayerName!)
            .Distinct()
            .OrderBy(n => n)
            .ToListAsync();
        return Ok(players);
    }

    [HttpGet("games")]
    public async Task<ActionResult<List<AdminGameSummary>>> GetGames(
        [FromQuery] string? playerName = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var query = db.Games.AsQueryable();
        if (!string.IsNullOrWhiteSpace(playerName))
            query = query.Where(g => g.PlayerName == playerName);

        var games = await query
            .OrderByDescending(g => g.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(g => new AdminGameSummary(
                g.Id,
                g.PlayerName,
                g.Status,
                g.MoveCount,
                g.WhiteAgentType,
                g.WhiteModel,
                g.BlackAgentType,
                g.BlackModel,
                g.CreatedAt,
                g.CompletedAt))
            .ToListAsync();

        return Ok(games);
    }

    [HttpGet("games/{id:guid}")]
    public async Task<ActionResult<AdminGameDetail>> GetGame(Guid id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var game = await db.Games
            .Include(g => g.Moves.OrderBy(m => m.Ply))
            .FirstOrDefaultAsync(g => g.Id == id);

        if (game == null) return NotFound();

        var detail = new AdminGameDetail(
            game.Id,
            game.PlayerName,
            game.Status,
            game.CurrentFen,
            game.MoveCount,
            game.WhiteAgentType,
            game.WhiteModel,
            game.WhitePersonality,
            game.BlackAgentType,
            game.BlackModel,
            game.BlackPersonality,
            game.Moves.Select(m => new AdminMoveItem(
                m.Ply, m.MoveNumber, m.Color, m.San,
                m.Evaluation, m.MaterialAdvantage,
                m.IsCheck, m.IsCheckmate, m.PlayedAt)).ToList(),
            game.CreatedAt,
            game.CompletedAt);

        return Ok(detail);
    }

    [HttpGet("prompts")]
    public async Task<ActionResult<List<AdminPromptLogItem>>> GetPrompts(
        [FromQuery] Guid? gameId = null,
        [FromQuery] string? playerName = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var query = db.PromptLogs
            .Include(p => p.Game)
            .AsQueryable();

        if (gameId.HasValue)
            query = query.Where(p => p.GameId == gameId.Value);

        if (!string.IsNullOrWhiteSpace(playerName))
            query = query.Where(p => p.Game.PlayerName == playerName);

        var prompts = await query
            .OrderByDescending(p => p.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new AdminPromptLogItem(
                p.Id,
                p.GameId,
                p.Game.PlayerName,
                p.Ply,
                p.Color,
                p.Model,
                p.Personality,
                p.PromptText,
                p.ResponseText,
                p.Success,
                p.CreatedAt))
            .ToListAsync();

        return Ok(prompts);
    }
}
