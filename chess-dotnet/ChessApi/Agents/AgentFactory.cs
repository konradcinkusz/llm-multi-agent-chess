using ChessApi.Config;
using Microsoft.Extensions.Options;
using OpenAI.Chat;

namespace ChessApi.Agents;

public class AgentFactory(IOptions<AppConfig> config)
{
    private readonly AppConfig _config = config.Value;

    public IChessAgent Create(string name, string agentType, string model, string personality)
    {
        if (agentType == "random") return new RandomChessAgent(name);

        var client = new ChatClient(model, new System.ClientModel.ApiKeyCredential(_config.OpenAiApiKey));
        return new LlmChessAgent(name, model, personality, client, config);
    }
}
