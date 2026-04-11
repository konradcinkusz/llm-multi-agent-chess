using System.Net.Http.Json;
using ChessBlazor.Models;

namespace ChessBlazor.Services;

public class ChessApiClient(HttpClient http)
{
    public async Task<GameResponse?> CreateGameAsync(AgentConfig white, AgentConfig black)
    {
        var payload = new
        {
            whiteAgentType = white.AgentType,
            whiteModel = white.Model,
            whitePersonality = white.Personality,
            blackAgentType = black.AgentType,
            blackModel = black.Model,
            blackPersonality = black.Personality,
        };
        var response = await http.PostAsJsonAsync("/api/games", payload);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<GameResponse>();
    }

    public async Task<GameResponse?> GetGameAsync(Guid id)
        => await http.GetFromJsonAsync<GameResponse>($"/api/games/{id}");

    public async Task<string?> GetBoardSvgAsync(Guid id)
    {
        var response = await http.GetAsync($"/api/games/{id}/board-svg");
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadAsStringAsync();
    }

    public async Task<string[]> GetModelsAsync()
        => await http.GetFromJsonAsync<string[]>("/api/config/models") ?? [];

    public async Task<string[]> GetPersonalitiesAsync()
        => await http.GetFromJsonAsync<string[]>("/api/config/personalities") ?? [];
}
