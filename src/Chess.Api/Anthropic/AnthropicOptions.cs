namespace Chess.Api.Anthropic;

/// <summary><c>Arena:Anthropic</c>: where the Anthropic API is and where the key typed into the app is kept.</summary>
public sealed class AnthropicOptions
{
    /// <summary>The Anthropic API. Absolute http or https URL.</summary>
    public string BaseUrl { get; set; } = "https://api.anthropic.com";

    /// <summary>
    /// The folder that holds the saved key, outside the repository (default: <c>llm-chess-arena</c> under the user's local
    /// application data). The key is encrypted with ASP.NET Core Data Protection, and the files are readable by the user only.
    /// </summary>
    public string? KeyFolder { get; set; }

    /// <summary>The longest wait for the Anthropic API when the key is saved or the models are refreshed.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(20);
}
