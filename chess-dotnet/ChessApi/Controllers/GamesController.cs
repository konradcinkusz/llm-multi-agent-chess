using ChessApi.DTOs;
using ChessApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace ChessApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GamesController(IGameService gameService) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<GameResponse>> CreateGame([FromBody] CreateGameRequest request)
    {
        var game = await gameService.CreateGameAsync(request);
        return CreatedAtAction(nameof(GetGame), new { id = game.Id }, game);
    }

    [HttpGet("{id:guid}")]
    public ActionResult<GameResponse> GetGame(Guid id)
    {
        var game = gameService.GetGame(id);
        return game == null ? NotFound() : Ok(game);
    }

    [HttpGet]
    public ActionResult<IEnumerable<GameSummary>> ListGames()
        => Ok(gameService.ListGames());

    [HttpDelete("{id:guid}")]
    public IActionResult AbandonGame(Guid id)
        => gameService.AbandonGame(id) ? NoContent() : NotFound();

    [HttpGet("{id:guid}/board-svg")]
    [Produces("image/svg+xml")]
    public ActionResult<string> GetBoardSvg(Guid id)
    {
        var session = gameService.GetSession(id);
        if (session == null) return NotFound();
        return Content(session.GetBoardSvg(), "image/svg+xml");
    }

    [HttpGet("{id:guid}/analysis")]
    public ActionResult<AnalysisDto> GetAnalysis(Guid id)
    {
        var session = gameService.GetSession(id);
        if (session == null) return NotFound();
        return Ok(DtoMapper.ToDto(session.GetAnalysis()));
    }
}
