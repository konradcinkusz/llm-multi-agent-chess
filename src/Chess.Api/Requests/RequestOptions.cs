using Chess.Api.Players.Prompts;
using Chess.Llm;
using Microsoft.Extensions.Options;

namespace Chess.Api.Requests;

/// <summary>Where chat requests come from. <c>Off</c> is the default; <c>Mock</c> is the fake profile's simulate form.</summary>
public enum RequestMode
{
    Off,
    Mock,
    YouTube,
}

/// <summary>
/// Bound from <c>Arena:Requests</c> (03 §15.2). The menu is what viewers may ask for; the tiers, the fee share and the
/// safety factor turn a game's estimated cost into the Super Chat price.
/// </summary>
public sealed class RequestOptions
{
    /// <summary>Whether chat intake is on. <c>Mock</c> enables the simulate endpoint, and only that.</summary>
    public RequestMode Mode { get; set; } = RequestMode.Off;

    /// <summary>The entries a viewer can name in a command. Empty is allowed only while <see cref="Mode"/> is <c>Off</c>.</summary>
    public List<RequestMenuEntry> Menu { get; set; } = [];

    /// <summary>
    /// When on (the default), every menu model must be a custom bring-your-own-key model from <c>Arena:CustomModels</c>, so a
    /// paid game never runs on the Copilot subscription (03 §15.2, ADR-016).
    /// </summary>
    public bool RequireApiKeyModels { get; set; } = true;

    /// <summary>The price tiers in USD. A matchup costs the smallest tier that covers its estimate.</summary>
    public List<double> Tiers { get; set; } = [1, 2, 5, 10, 20, 50];

    /// <summary>YouTube's share of a Super Chat. The game's budget is the price less this share.</summary>
    public double FeeShare { get; set; } = 0.30;

    /// <summary>Headroom on the estimate, so a game that runs long still fits its tier.</summary>
    public double SafetyFactor { get; set; } = 1.5;

    /// <summary>The estimated minutes a queued game takes, for the wait the queue reports.</summary>
    public double EstimatedMinutesPerGame { get; set; } = 20;

    /// <summary>Tokens a move is assumed to take in each prompt style, when a model has no stored moves.</summary>
    public Dictionary<string, StyleTokens> Styles { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["classic"] = new() { InputTokens = 700, OutputTokens = 300 },
        ["board"] = new() { InputTokens = 1600, OutputTokens = 300 },
        ["think"] = new() { InputTokens = 2000, OutputTokens = 900 },
    };

    /// <summary>
    /// US dollars per unit of each Super Chat currency (ISO 4217). The defaults are placeholders; the owner updates the rates.
    /// A currency missing from the table is recorded at 0 USD with the reason "currency not configured".
    /// </summary>
    public Dictionary<string, double> UsdPerUnit { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["USD"] = 1.0,
        ["EUR"] = 1.10,
        ["GBP"] = 1.30,
        ["CAD"] = 0.73,
        ["AUD"] = 0.66,
        ["NZD"] = 0.60,
        ["BRL"] = 0.19,
        ["MXN"] = 0.055,
        ["INR"] = 0.012,
        ["JPY"] = 0.0067,
        ["KRW"] = 0.00074,
        ["PLN"] = 0.26,
        ["SEK"] = 0.095,
    };
}

/// <summary>One menu entry: a code the viewer types, the player it names (<c>model</c> or <c>model@effort</c>) and its label.</summary>
public sealed class RequestMenuEntry
{
    public string Code { get; set; } = "";

    public string PlayerId { get; set; } = "";

    /// <summary>The name shown in the menu. Defaults to <see cref="PlayerId"/>.</summary>
    public string? Label { get; set; }
}

/// <summary>Tokens a move takes in one prompt style, in input and output tokens (03 §15.2).</summary>
public sealed class StyleTokens
{
    public double InputTokens { get; set; }

    public double OutputTokens { get; set; }
}

/// <summary>
/// Checks <c>Arena:Requests</c> when the host starts, so a bad entry stops startup with a message that names it. Under
/// <c>RequireApiKeyModels</c>, each menu model must be one of <c>Arena:CustomModels</c>: a Copilot model fails here.
/// </summary>
public sealed class RequestOptionsValidator(IOptions<ArenaOptions> arena) : IValidateOptions<RequestOptions>
{
    public ValidateOptionsResult Validate(string? name, RequestOptions options)
    {
        var errors = new List<string>();
        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var customIds = arena.Value.CustomModels.Select(m => m.Id).ToHashSet(StringComparer.Ordinal);

        if (options.Mode != RequestMode.Off && options.Menu.Count == 0)
        {
            errors.Add("Arena:Requests:Menu is empty, so no request could be quoted. Add at least one entry.");
        }

        for (var i = 0; i < options.Menu.Count; i++)
        {
            var entry = options.Menu[i];
            var at = $"Arena:Requests:Menu[{i}]";

            if (string.IsNullOrWhiteSpace(entry.Code) || entry.Code.Any(char.IsWhiteSpace) || entry.Code.IndexOfAny(['@', '~']) >= 0)
            {
                errors.Add($"{at}.Code is required and must contain no spaces, '@' or '~'.");
            }
            else if (!codes.Add(entry.Code))
            {
                errors.Add($"{at}.Code '{entry.Code}' is already used by another menu entry.");
            }

            if (string.IsNullOrWhiteSpace(entry.PlayerId) || entry.PlayerId.Contains('~'))
            {
                errors.Add($"{at}.PlayerId is required and must not contain '~': the style is chosen in the command.");
                continue;
            }

            try
            {
                ModelVariant.Parse(entry.PlayerId);
            }
            catch (ArgumentException)
            {
                errors.Add($"{at}.PlayerId must be model or model@effort.");
                continue;
            }

            if (options.RequireApiKeyModels && !customIds.Contains(ModelVariant.BaseOf(entry.PlayerId)))
            {
                errors.Add($"{at}.PlayerId '{ModelVariant.BaseOf(entry.PlayerId)}' is not in Arena:CustomModels. With Arena:Requests:RequireApiKeyModels on, every menu model must be a bring-your-own-key model.");
            }
        }

        if (options.Tiers.Count == 0 || options.Tiers.Any(t => !double.IsFinite(t) || t <= 0))
        {
            errors.Add("Arena:Requests:Tiers must list one or more positive USD amounts.");
        }

        if (!double.IsFinite(options.FeeShare) || options.FeeShare < 0 || options.FeeShare >= 1)
        {
            errors.Add("Arena:Requests:FeeShare must be at least 0 and below 1.");
        }

        if (!double.IsFinite(options.SafetyFactor) || options.SafetyFactor < 1)
        {
            errors.Add("Arena:Requests:SafetyFactor must be at least 1.");
        }

        if (!double.IsFinite(options.EstimatedMinutesPerGame) || options.EstimatedMinutesPerGame <= 0)
        {
            errors.Add("Arena:Requests:EstimatedMinutesPerGame must be positive.");
        }

        foreach (var style in PromptStyles.All)
        {
            if (options.Styles.TryGetValue(style.Id, out var tokens) && tokens.InputTokens > 0 && tokens.OutputTokens > 0)
            {
                continue;
            }

            errors.Add($"Arena:Requests:Styles must give '{style.Id}' positive input and output tokens.");
        }

        foreach (var (currency, rate) in options.UsdPerUnit)
        {
            if (currency.Length != 3 || !currency.All(char.IsLetter) || !double.IsFinite(rate) || rate <= 0)
            {
                errors.Add($"Arena:Requests:UsdPerUnit '{currency}' must be a three-letter currency code with a positive rate.");
            }
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
