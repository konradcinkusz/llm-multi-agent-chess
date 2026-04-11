using System.Collections.Concurrent;
using ChessApi.Agents;
using ChessApi.Data;
using ChessApi.DTOs;
using ChessApi.Entities;
using ChessApi.Sessions;
using Microsoft.EntityFrameworkCore;

namespace ChessApi.Services;

public class GameService(AgentFactory agentFactory, IDbContextFactory<AppDbContext> dbFactory) : IGameService
{
    private readonly ConcurrentDictionary<Guid, ChessGameSession> _sessions = new();

    public async Task<GameResponse> CreateGameAsync(CreateGameRequest req)
    {
        var id = Guid.NewGuid();
        var white = agentFactory.Create("White", req.WhiteAgentType, req.WhiteModel, req.WhitePersonality);
        var black = agentFactory.Create("Black", req.BlackAgentType, req.BlackModel, req.BlackPersonality);
        var session = new ChessGameSession(id, white, black);
        _sessions[id] = session;

        await using var db = await dbFactory.CreateDbContextAsync();
        db.Games.Add(new Game
        {
            Id = id,
            CreatedAt = DateTime.UtcNow,
            WhiteAgentType = req.WhiteAgentType,
            WhiteModel = req.WhiteModel,
            WhitePersonality = req.WhitePersonality,
            BlackAgentType = req.BlackAgentType,
            BlackModel = req.BlackModel,
            BlackPersonality = req.BlackPersonality,
        });
        await db.SaveChangesAsync();

        return BuildResponse(session);
    }

    public GameResponse? GetGame(Guid id)
        => _sessions.TryGetValue(id, out var s) ? BuildResponse(s) : null;

    public IEnumerable<GameSummary> ListGames()
        => _sessions.Values
            .OrderByDescending(s => s.Id)
            .Take(20)
            .Select(s => new GameSummary(s.Id, s.GetStatusText(), s.MoveCount,
                s.WhiteAgent.AgentType, s.BlackAgent.AgentType, DateTime.UtcNow));

    public bool AbandonGame(Guid id)
    {
        if (!_sessions.TryRemove(id, out _)) return false;
        _ = Task.Run(async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var game = await db.Games.FindAsync(id);
            if (game != null) { game.Status = "Abandoned"; game.CompletedAt = DateTime.UtcNow; await db.SaveChangesAsync(); }
        });
        return true;
    }

    public ChessGameSession? GetSession(Guid id)
        => _sessions.GetValueOrDefault(id);

    public async Task PersistMoveAsync(Guid gameId, MoveResult result)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var game = await db.Games.FindAsync(gameId);
        if (game == null) return;

        game.CurrentFen = result.Fen;
        game.MoveCount = result.Ply;
        game.Status = result.StatusText;

        db.Moves.Add(new MoveRecord
        {
            GameId = gameId,
            MoveNumber = result.MoveNumber,
            Ply = result.Ply,
            Color = result.Color,
            San = result.San,
            Evaluation = result.Evaluation,
            MaterialAdvantage = result.MaterialAdvantage,
            IsCheck = result.IsCheck,
            IsCheckmate = result.IsCheckmate,
            PlayedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
    }

    public async Task FinalizeGameAsync(Guid gameId, string status)
    {
        _sessions.TryRemove(gameId, out _);
        await using var db = await dbFactory.CreateDbContextAsync();
        var game = await db.Games.FindAsync(gameId);
        if (game != null)
        {
            game.Status = status;
            game.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
    }

    private static GameResponse BuildResponse(ChessGameSession s)
    {
        var analysis = s.GetAnalysis();
        return new GameResponse(
            Id: s.Id,
            Status: s.GetStatusText(),
            CurrentFen: s.GetFen(),
            MoveCount: s.MoveCount,
            CurrentTurn: s.CurrentTurn,
            IsGameOver: s.IsGameOver,
            IsThinking: s.IsThinking,
            White: new AgentInfo(s.WhiteAgent.AgentType, s.WhiteAgent.Model, s.WhiteAgent.Personality),
            Black: new AgentInfo(s.BlackAgent.AgentType, s.BlackAgent.Model, s.BlackAgent.Personality),
            History: s.History.Select(h => new MoveHistoryItem(h.MoveNumber, h.Color, h.San)).ToList(),
            Analysis: DtoMapper.ToDto(analysis),
            CreatedAt: DateTime.UtcNow);
    }
}
