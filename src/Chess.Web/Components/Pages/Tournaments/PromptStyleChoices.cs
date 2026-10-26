using Chess.Web.Api;

namespace Chess.Web.Components.Pages.Tournaments;

/// <summary>
/// The prompt styles the forms offer (03 §14.4, ADR-015). The list comes from <c>GET /api/prompt-styles</c>; when that
/// fails the forms offer <c>classic</c> alone and say so. Every player id the forms send is built here with
/// <see cref="PlayerId.Compose"/>, so a classic player keeps its plain id.
/// </summary>
public static class PromptStyleChoices
{
    /// <summary>The style the forms start on, and the only one offered when the list cannot be loaded.</summary>
    public static readonly PromptStyleDto Classic = new(PlayerId.DefaultStyle, "Classic",
        "Today's rated prompt: the position as a FEN, the last ten plies and the legal moves. One SAN a move.", false, "");

    /// <summary>What a form says when the styles could not be loaded.</summary>
    public const string Unavailable = "Prompt styles could not be loaded, so only classic is offered.";

    public const string RandomModelId = CostPreview.RandomModelId;

    /// <summary>The styles to offer, and the reason they are only classic when they could not be loaded.</summary>
    public sealed record Loaded(IReadOnlyList<PromptStyleDto> Styles, string? Notice);

    /// <summary>The styles from the API, in its order. Any failure, or an empty list, leaves classic alone with a notice.</summary>
    public static async Task<Loaded> LoadAsync(ArenaApiClient api, CancellationToken ct)
    {
        try
        {
            var styles = await api.GetPromptStylesAsync(ct);
            return styles.Count > 0 ? new(styles, null) : new([Classic], Unavailable);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return new([Classic], Unavailable);
        }
    }

    /// <summary>The player id of a model in a style, with its effort when it has one. The random player has no styles.</summary>
    public static string Player(string modelId, string? effort, string style) =>
        modelId == RandomModelId ? modelId : PlayerId.Compose(modelId, effort, style);

    /// <summary>
    /// Every model in every style, model by model, so that a round robin compares the styles. The random player is one
    /// player whatever the styles.
    /// </summary>
    public static IReadOnlyList<string> Participants(IEnumerable<string> models, IEnumerable<string> styles)
    {
        var chosen = styles.ToArray();
        return [.. models.SelectMany(model => model == RandomModelId
            ? [Player(model, null, PlayerId.DefaultStyle)]
            : chosen.Select(style => Player(model, null, style)))];
    }

    /// <summary>The model a player id names: <c>gpt</c> for <c>gpt@high~think</c>.</summary>
    public static string ModelOf(string playerId) => PlayerId.Parse(playerId).Model;

    /// <summary>
    /// The model with its id replaced by the player's, so its prices and token history are looked up under that player
    /// (03 §14.4). Null when the catalogue has no such model.
    /// </summary>
    public static ModelDto? Find(IReadOnlyList<ModelDto>? models, string playerId) =>
        models?.FirstOrDefault(model => model.Id == ModelOf(playerId)) is { } model ? model with { Id = playerId } : null;

    /// <summary>A player's name: its model's display name, then its style's when the style is not classic.</summary>
    public static string Label(string playerId, IReadOnlyList<ModelDto>? models, IReadOnlyList<PromptStyleDto> styles)
    {
        var player = PlayerId.Parse(playerId);
        var name = models?.FirstOrDefault(model => model.Id == player.Model)?.DisplayName ?? player.Model;
        if (player.Style == PlayerId.DefaultStyle)
        {
            return name;
        }

        var style = styles.FirstOrDefault(entry => entry.Id == player.Style)?.DisplayName ?? player.Style;
        return $"{name} · {style}";
    }

    /// <summary>The description of a style, shown as the hint under its select. Empty when the style is not listed.</summary>
    public static string Describe(IReadOnlyList<PromptStyleDto> styles, string styleId) =>
        styles.FirstOrDefault(style => style.Id == styleId)?.Description ?? "";

    /// <summary>"1 player" or "3 players", the count a style chip shows.</summary>
    public static string Players(int count) => count == 1 ? "1 player" : $"{count} players";
}
