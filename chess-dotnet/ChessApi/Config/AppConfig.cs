namespace ChessApi.Config;

public class AppConfig
{
    public string OpenAiApiKey { get; set; } = "";
    public string DefaultModel { get; set; } = "gpt-4o-mini";
    public string[] AlternativeModels { get; set; } =
        ["gpt-4o-mini", "gpt-4o", "gpt-3.5-turbo", "gpt-4"];
    public string[] AgentPersonalities { get; set; } =
        ["standard", "aggressive", "defensive"];
    public int MaxRetries { get; set; } = 3;
    public float Temperature { get; set; } = 0.3f;
    public float AutoPlayDelay { get; set; } = 1.0f;
    public int MoveHistoryLimit { get; set; } = 5;
}
