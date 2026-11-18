namespace Chess.Llm;

/// <summary>Configuration section <c>Copilot</c>. No value here is ever logged.</summary>
public sealed class CopilotOptions
{
    /// <summary>Path to a Copilot CLI executable. Null uses the runtime bundled with the SDK.</summary>
    public string? CliPath { get; set; }

    /// <summary>URL of an already running Copilot runtime (<c>port</c>, <c>host:port</c> or a full URL). Takes precedence over <see cref="CliPath"/>.</summary>
    public string? CliUrl { get; set; }

    /// <summary>Optional GitHub token. When set it replaces the logged-in user (an Aspire secret parameter in the AppHost).</summary>
    public string? GitHubToken { get; set; }

    /// <summary>How long the runtime may take to start, authenticate and list models before the gateway reports Unavailable.</summary>
    public TimeSpan StartupTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How long opening one conversation (an SDK session) may take. Board and think styles open one for every move.</summary>
    public TimeSpan SessionStartTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
