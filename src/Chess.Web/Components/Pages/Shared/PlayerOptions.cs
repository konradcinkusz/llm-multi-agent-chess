using Chess.Web.Api;
using Chess.Web.Components.Pages.Tournaments;

namespace Chess.Web.Components.Pages.Shared;

/// <summary>One entry of a player picker: the id the API matches exactly, and the name shown for it.</summary>
public sealed record PlayerOption(string Id, string Label);

/// <summary>
/// The players the pickers of Games, Head-to-head and Attempts offer. The API ranks and filters by player id
/// (<c>model[@effort][~style]</c>, 03 §12.1, §14.1), not by catalogue model, so a model that played at an effort or in a style
/// is several players. The list comes from <c>GET /api/ratings</c>; when there are no ratings it is the catalogue.
/// </summary>
public static class PlayerOptions
{
    /// <summary>
    /// The rated players in the order of the ranking and, with <paramref name="includeCatalogue"/>, then the catalogue models
    /// that have no rating (a model can have played unrated games only). With no ratings the catalogue alone is offered.
    /// </summary>
    public static async Task<IReadOnlyList<PlayerOption>> LoadAsync(ArenaApiClient api, bool includeCatalogue, CancellationToken ct)
    {
        var board = await Quietly(() => api.GetLeaderboardAsync(ct), ct);
        var rated = board?.Models ?? [];
        if (rated.Count == 0)
        {
            // No ratings yet: the catalogue is all there is to pick from. When it cannot be loaded either, the page is told.
            return From(await api.GetModelsAsync(ct));
        }

        var models = await Quietly(() => api.GetModelsAsync(ct), ct);
        var styles = (await PromptStyleChoices.LoadAsync(api, ct)).Styles;
        var options = rated.Select(player => new PlayerOption(player.Model, Label(player.Model, models, styles))).ToList();
        if (includeCatalogue && models is not null)
        {
            var known = options.Select(option => option.Id).ToHashSet(StringComparer.Ordinal);
            options.AddRange(From(models).Where(option => !known.Contains(option.Id)));
        }

        return options;
    }

    /// <summary>The catalogue as options, named as the pickers named a model before players: by its display name.</summary>
    public static IReadOnlyList<PlayerOption> From(IEnumerable<ModelDto> models) =>
        [.. models.Select(model => new PlayerOption(model.Id, model.Enabled ? model.DisplayName : $"{model.DisplayName} (disabled)"))];

    /// <summary>
    /// A player's name: its model's display name, then its effort and its style when it has them, so
    /// <c>gpt</c>, <c>gpt@high</c> and <c>gpt~think</c> read differently.
    /// </summary>
    public static string Label(string playerId, IReadOnlyList<ModelDto>? models, IReadOnlyList<PromptStyleDto> styles)
    {
        var player = PlayerId.Parse(playerId);
        var name = models?.FirstOrDefault(model => model.Id == player.Model)?.DisplayName ?? player.Model;
        if (!string.IsNullOrEmpty(player.Effort))
        {
            name += $" @ {player.Effort}";
        }

        if (player.Style != PlayerId.DefaultStyle)
        {
            name += $" · {styles.FirstOrDefault(style => style.Id == player.Style)?.DisplayName ?? player.Style}";
        }

        return name;
    }

    private static async Task<T?> Quietly<T>(Func<Task<T>> load, CancellationToken ct) where T : class
    {
        try
        {
            return await load();
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // A picker is a convenience: when a list cannot be loaded the page still works with what it has.
            return null;
        }
    }
}
