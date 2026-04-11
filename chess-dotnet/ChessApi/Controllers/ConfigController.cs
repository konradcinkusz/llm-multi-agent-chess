using ChessApi.Config;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ChessApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ConfigController(IOptions<AppConfig> config) : ControllerBase
{
    [HttpGet("models")]
    public ActionResult<string[]> GetModels()
        => Ok(config.Value.AlternativeModels);

    [HttpGet("personalities")]
    public ActionResult<string[]> GetPersonalities()
        => Ok(config.Value.AgentPersonalities);
}
