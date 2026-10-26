using Microsoft.Extensions.Options;

namespace Chess.Llm;

/// <summary>
/// Configuration section <c>Arena</c>; this project reads <see cref="CustomModels"/>. A custom model's API key is a
/// secret (an AppHost secret parameter or user secrets): it is never logged, printed or stored (ADR-014).
/// </summary>
public sealed class ArenaOptions
{
    /// <summary>Models served by an OpenAI-compatible or Azure endpoint. They are listed next to the Copilot models.</summary>
    public List<CustomModelOptions> CustomModels { get; set; } = [];
}

/// <summary>One entry of <c>Arena:CustomModels</c>. <see cref="ToString"/> leaves the API key out.</summary>
public sealed class CustomModelOptions
{
    /// <summary>The player id. No <c>@</c> or spaces, unique, and not the id of a Copilot model.</summary>
    public string Id { get; set; } = "";

    /// <summary>The name shown in the UI. Defaults to <see cref="Id"/>.</summary>
    public string? DisplayName { get; set; }

    /// <summary><c>openai</c> or <c>azure</c>: the SDK's provider type.</summary>
    public string ProviderType { get; set; } = "openai";

    /// <summary>Absolute http or https URL of the provider's API.</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>The model name the endpoint expects: a model name, or an Azure deployment or fine-tune name.</summary>
    public string Model { get; set; } = "";

    /// <summary>The wire API format. <c>chat-completions</c> unless the endpoint needs another.</summary>
    public string WireApi { get; set; } = "chat-completions";

    /// <summary>The endpoint's API key. Optional for an endpoint that takes none.</summary>
    public string? ApiKey { get; set; }

    public override string ToString() => $"CustomModel {Id} ({ProviderType}, model {Model})";
}

/// <summary>
/// Checks <c>Arena:CustomModels</c> when the host starts, so a bad entry stops startup with a message that names it.
/// Messages never quote a base URL or an API key. A custom id that a Copilot model already uses is checked later,
/// when the runtime lists the Copilot models (see <see cref="CopilotLlmGateway"/>).
/// </summary>
internal sealed class ArenaOptionsValidator : IValidateOptions<ArenaOptions>
{
    private static readonly string[] ProviderTypes = ["openai", "azure"];

    public ValidateOptionsResult Validate(string? name, ArenaOptions options)
    {
        var errors = new List<string>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < options.CustomModels.Count; i++)
        {
            var model = options.CustomModels[i];
            var at = $"Arena:CustomModels[{i}]";

            if (string.IsNullOrWhiteSpace(model.Id))
            {
                errors.Add($"{at}.Id is required.");
            }
            else
            {
                if (model.Id.Contains('@'))
                {
                    errors.Add($"{at}.Id '{model.Id}' must not contain '@': that separates the reasoning effort (ADR-014).");
                }

                if (model.Id.Contains('~'))
                {
                    errors.Add($"{at}.Id '{model.Id}' must not contain '~': that separates the prompt style (ADR-015).");
                }

                if (model.Id.Any(char.IsWhiteSpace))
                {
                    errors.Add($"{at}.Id '{model.Id}' must not contain spaces.");
                }

                if (!ids.Add(model.Id))
                {
                    errors.Add($"{at}.Id '{model.Id}' is already used by another custom model.");
                }
            }

            if (!ProviderTypes.Contains(model.ProviderType, StringComparer.OrdinalIgnoreCase))
            {
                errors.Add($"{at}.ProviderType must be 'openai' or 'azure'.");
            }

            if (!Uri.TryCreate(model.BaseUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            {
                errors.Add($"{at}.BaseUrl must be an absolute http or https URL.");
            }

            if (string.IsNullOrWhiteSpace(model.Model))
            {
                errors.Add($"{at}.Model is required.");
            }

            if (string.IsNullOrWhiteSpace(model.WireApi))
            {
                errors.Add($"{at}.WireApi is required.");
            }
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
