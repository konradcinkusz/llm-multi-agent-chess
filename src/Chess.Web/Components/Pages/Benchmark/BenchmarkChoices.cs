using Chess.Web.Api;
using Chess.Web.Components.Pages.Tournaments;

namespace Chess.Web.Components.Pages.Benchmark;

/// <summary>
/// What the benchmark form offers, from <c>GET /api/models</c> (03 §12.1, §12.3). The rules are the API's
/// (<c>BenchmarkRules</c> in Chess.Api): the Web project may not reference it (P3), so these are copies and the
/// benchmark tests hold them to the same answers.
/// </summary>
public static class BenchmarkChoices
{
    /// <summary>The auto-router. The API leaves it out of the default opponents (<c>ModelCatalog.AutoModelId</c>).</summary>
    public const string AutoModelId = "auto";

    /// <summary>The separator of a player id that carries an effort: <c>model@effort</c>.</summary>
    public const char EffortSeparator = '@';

    /// <summary>The models that can be the candidate: enabled, and not the random player, which the API refuses.</summary>
    public static IReadOnlyList<ModelDto> Candidates(IEnumerable<ModelDto> models) =>
        models.Where(model => model.Enabled && model.Id != CostPreview.RandomModelId).ToArray();

    /// <summary>
    /// The opponents the form offers: enabled, not the candidate's model whatever its effort, and not Auto. The random
    /// player is an opponent like any other. "All" plays exactly this list.
    /// </summary>
    public static IReadOnlyList<ModelDto> Opponents(IEnumerable<ModelDto> models, string candidateModelId) =>
        models.Where(model => model.Enabled && model.Id != candidateModelId && !IsAuto(model.Id)).ToArray();

    /// <summary>The player id of a candidate: <c>model@effort</c> when it has an effort, else the model id alone.</summary>
    public static string Variant(string modelId, string? effort) =>
        string.IsNullOrEmpty(effort) ? modelId : modelId + EffortSeparator + effort;

    /// <summary>The effort a model starts on: its default when it lists one, else its first listed effort.</summary>
    public static string? DefaultEffort(ModelDto model) =>
        model.DefaultReasoningEffort ?? model.ReasoningEfforts?.FirstOrDefault();

    /// <summary>The model a player id names: <c>claude-x</c> for <c>claude-x@high</c>.</summary>
    public static string ModelOf(string playerId) =>
        playerId.Split(EffortSeparator, 2)[0];

    /// <summary>A model's name in a list: its display name, and "(custom)" when it is served by a custom endpoint.</summary>
    public static string Label(ModelDto model) =>
        model.Custom ? $"{model.DisplayName} (custom)" : model.DisplayName;

    private static bool IsAuto(string modelId) =>
        modelId.Equals(AutoModelId, StringComparison.OrdinalIgnoreCase);
}
