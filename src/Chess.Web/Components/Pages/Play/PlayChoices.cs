using Chess.Web.Api;
using Chess.Web.Components.Pages.Tournaments;

namespace Chess.Web.Components.Pages.Play;

/// <summary>A model in a player picker. Without a Copilot login only Random can be picked.</summary>
public sealed record ModelChoice(ModelDto Model, bool Selectable);

/// <summary>What the live page offers and how it words it. Pure, so the rules can be tested without a page.</summary>
public static class PlayChoices
{
    public const string Random = "random";

    /// <summary>Copilot is Ready when signed in, and Fake when the fake gateway runs (development and E2E).</summary>
    public static bool GatewayReady(StatusDto? status) => status?.Copilot is "Ready" or "Fake";

    /// <summary>Every enabled model; the LLMs are only selectable once the gateway is ready.</summary>
    public static IReadOnlyList<ModelChoice> Choices(IReadOnlyList<ModelDto> models, bool gatewayReady) =>
        models.Where(model => model.Enabled)
            .Select(model => new ModelChoice(model, gatewayReady || model.Id == Random))
            .ToArray();

    /// <summary>The first LLM for White, the second for Black; Random when there is no such model.</summary>
    public static string Default(IReadOnlyList<ModelChoice> choices, int side)
    {
        var llms = choices.Where(choice => choice.Selectable && choice.Model.Id != Random).ToArray();
        return side < llms.Length ? llms[side].Model.Id : Random;
    }

    public static PlayerSpec Player(string modelId) =>
        modelId == Random ? new("random", Random) : new("llm", modelId);

    public static string Label(ModelDto model) =>
        model.Id == Random ? model.DisplayName : $"{model.DisplayName} · {TournamentFormat.Multiplier(model.PremiumMultiplier)}";

    /// <summary>"1-0 · checkmate"; the termination is left out when the server did not send one.</summary>
    public static string Outcome(GameOutcome outcome) =>
        string.IsNullOrEmpty(outcome.Termination)
            ? outcome.Result
            : $"{outcome.Result} · {Termination(outcome.Termination)}";

    public static string Termination(string termination) => termination switch
    {
        "checkmate" => "checkmate",
        "stalemate" => "stalemate",
        "insufficient-material" => "insufficient material",
        "fifty-move" => "fifty-move rule",
        "threefold" => "threefold repetition",
        "forfeit-white" => "White forfeited",
        "forfeit-black" => "Black forfeited",
        "adjudicated" => "adjudicated",
        "aborted" => "aborted",
        "interrupted" => "interrupted",
        _ => termination,
    };

    /// <summary>Explains a forfeit, the one ending a player can cause by illegal replies alone. Null for any other ending.</summary>
    public static string? ForfeitNotice(string? termination) => termination switch
    {
        "forfeit-white" => "White forfeited: no legal move within the attempt limit. No fallback move is played.",
        "forfeit-black" => "Black forfeited: no legal move within the attempt limit. No fallback move is played.",
        _ => null,
    };
}
