using System.Net.Http.Json;
using ChessBlazor.Models;

namespace ChessBlazor.Services;

public class AdminApiClient(HttpClient http)
{
    public async Task<AdminStats?> GetStatsAsync()
        => await http.GetFromJsonAsync<AdminStats>("/api/admin/stats");

    public async Task<List<string>> GetPlayersAsync()
        => await http.GetFromJsonAsync<List<string>>("/api/admin/players") ?? [];

    public async Task<List<AdminGameSummary>> GetGamesAsync(string? playerName = null, int page = 1, int pageSize = 50)
    {
        var url = $"/api/admin/games?page={page}&pageSize={pageSize}";
        if (!string.IsNullOrWhiteSpace(playerName))
            url += $"&playerName={Uri.EscapeDataString(playerName)}";
        return await http.GetFromJsonAsync<List<AdminGameSummary>>(url) ?? [];
    }

    public async Task<AdminGameDetail?> GetGameDetailAsync(Guid id)
        => await http.GetFromJsonAsync<AdminGameDetail>($"/api/admin/games/{id}");

    public async Task<List<AdminPromptLogItem>> GetPromptsAsync(
        Guid? gameId = null, string? playerName = null, int page = 1, int pageSize = 50)
    {
        var url = $"/api/admin/prompts?page={page}&pageSize={pageSize}";
        if (gameId.HasValue)
            url += $"&gameId={gameId}";
        if (!string.IsNullOrWhiteSpace(playerName))
            url += $"&playerName={Uri.EscapeDataString(playerName)}";
        return await http.GetFromJsonAsync<List<AdminPromptLogItem>>(url) ?? [];
    }
}
