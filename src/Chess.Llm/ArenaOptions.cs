using Microsoft.Extensions.Options;

namespace Chess.Llm;

/// <summary>
/// Configuration section <c>Arena</c>; this project reads <see cref="CustomModels"/>. A custom model's API key is a
/// secret (an AppHost secret parameter or user secrets): it is never logged, printed or stored (ADR-014).
/// </summary>
public sealed class ArenaOptions
{
    /// <summary>Models served by an Anthropic, OpenAI-compatible or Azure endpoint. They are listed next to the Copilot models.</summary>
    public List<CustomModelOptions> CustomModels { get; set; } = [];

    /// <summary>The cost settings this project reads: the rate that turns USD prices into AI credits.</summary>
    public ArenaCostOptions Cost { get; set; } = new();
}

/// <summary>
/// <c>Arena:Cost</c>, the same section the API binds. Only the rate is read here, to price a custom model given in US
/// dollars in AI credits (03 §10, §12.1).
/// </summary>
public sealed class ArenaCostOptions
{
    /// <summary>The default of <c>Arena:Cost:UsdPerAiCredit</c>.</summary>
    public const double DefaultUsdPerAiCredit = 0.01;

    /// <summary>The dollar value of one AI credit.</summary>
    public double UsdPerAiCredit { get; set; } = DefaultUsdPerAiCredit;
}

/// <summary>One entry of <c>Arena:CustomModels</c>. <see cref="ToString"/> leaves the API key out.</summary>
public sealed class CustomModelOptions
{
    /// <summary>The player id. No <c>@</c> or spaces, unique, and not the id of a Copilot model.</summary>
    public string Id { get; set; } = "";

    /// <summary>The name shown in the UI. Defaults to <see cref="Id"/>.</summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// <c>anthropic</c> (the Messages API), <c>openai</c> (any OpenAI-compatible endpoint, OpenAI and DeepInfra included) or
    /// <c>azure</c>: the SDK's provider type.
    /// </summary>
    public string ProviderType { get; set; } = "openai";

    /// <summary>Absolute http or https URL of the provider's API, e.g. <c>https://api.anthropic.com</c>.</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>The model name the endpoint expects: a model name, or an Azure deployment or fine-tune name.</summary>
    public string Model { get; set; } = "";

    /// <summary>
    /// The wire API format. <c>chat-completions</c> unless the endpoint needs another. The SDK reads it for <c>openai</c>
    /// and <c>azure</c> only; an <c>anthropic</c> session does not send it.
    /// </summary>
    public string WireApi { get; set; } = "chat-completions";

    /// <summary>The endpoint's API key. Optional for an endpoint that takes none.</summary>
    public string? ApiKey { get; set; }

    /// <summary>The price of one million input tokens in US dollars. Set it with <see cref="UsdPerMillionOutput"/>, or not at all.</summary>
    public double? UsdPerMillionInput { get; set; }

    /// <summary>The price of one million output tokens in US dollars. Set it with <see cref="UsdPerMillionInput"/>, or not at all.</summary>
    public double? UsdPerMillionOutput { get; set; }

    /// <summary>The price of one million input tokens in AI credits. Set it with <see cref="CreditsPerMillionOutput"/>, or not at all.</summary>
    public double? CreditsPerMillionInput { get; set; }

    /// <summary>The price of one million output tokens in AI credits. Set it with <see cref="CreditsPerMillionInput"/>, or not at all.</summary>
    public double? CreditsPerMillionOutput { get; set; }

    public override string ToString() => $"CustomModel {Id} ({ProviderType}, model {Model})";
}

/// <summary>
/// Checks <c>Arena:CustomModels</c> when the host starts, so a bad entry stops startup with a message that names it.
/// Messages never quote a base URL or an API key. A custom id that a Copilot model already uses is checked later,
/// when the runtime lists the Copilot models (see <see cref="CopilotLlmGateway"/>).
/// </summary>
internal sealed class ArenaOptionsValidator : IValidateOptions<ArenaOptions>
{
    private static readonly string[] ProviderTypes = ["anthropic", "openai", "azure"];

    public ValidateOptionsResult Validate(string? name, ArenaOptions options)
    {
        var errors = new List<string>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!double.IsFinite(options.Cost.UsdPerAiCredit) || options.Cost.UsdPerAiCredit <= 0)
        {
            errors.Add("Arena:Cost:UsdPerAiCredit must be a positive number.");
        }

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
                errors.Add($"{at}.ProviderType '{model.ProviderType}' is not supported. Use 'anthropic', 'openai' or 'azure'.");
            }

            if (!Uri.TryCreate(model.BaseUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            {
                errors.Add(string.Equals(model.ProviderType, "anthropic", StringComparison.OrdinalIgnoreCase)
                    ? $"{at}.BaseUrl is required for anthropic: an absolute http or https URL, such as https://api.anthropic.com."
                    : $"{at}.BaseUrl must be an absolute http or https URL.");
            }

            if (string.IsNullOrWhiteSpace(model.Model))
            {
                errors.Add($"{at}.Model is required.");
            }

            if (string.IsNullOrWhiteSpace(model.WireApi) && !string.Equals(model.ProviderType, "anthropic", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"{at}.WireApi is required.");
            }

            CheckPricePair(errors, at, "UsdPerMillionInput", model.UsdPerMillionInput, "UsdPerMillionOutput", model.UsdPerMillionOutput);
            CheckPricePair(errors, at, "CreditsPerMillionInput", model.CreditsPerMillionInput, "CreditsPerMillionOutput", model.CreditsPerMillionOutput);
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    /// <summary>A price is set as a pair (input and output) or not at all, and a set price is a finite number of zero or more.</summary>
    private static void CheckPricePair(List<string> errors, string at, string inputName, double? input, string outputName, double? output)
    {
        if (input.HasValue != output.HasValue)
        {
            errors.Add($"{at}: set {inputName} and {outputName} together, or neither.");
        }

        CheckPrice(errors, at, inputName, input);
        CheckPrice(errors, at, outputName, output);
    }

    private static void CheckPrice(List<string> errors, string at, string name, double? price)
    {
        if (price is { } value && (!double.IsFinite(value) || value < 0))
        {
            errors.Add($"{at}.{name} must be a finite number of zero or more.");
        }
    }
}
