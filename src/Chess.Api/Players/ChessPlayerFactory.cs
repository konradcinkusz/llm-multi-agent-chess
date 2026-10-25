using Chess.Api.Catalog;
using Chess.Engine;
using Chess.Llm;
using Microsoft.Extensions.Options;

namespace Chess.Api.Players;

/// <summary>Creates the player for a side: <c>llm</c> opens a conversation on the gateway, <c>random</c> is local.</summary>
public sealed class ChessPlayerFactory(ILlmGateway gateway, IOptions<PlayerOptions> options) : IChessPlayerFactory
{
    public async Task<IChessPlayer> CreateAsync(PlayerDescriptor descriptor, Guid gameId, Side side, CancellationToken ct)
    {
        var settings = options.Value;
        switch (descriptor.Kind)
        {
            case "random":
                return new RandomChessPlayer(descriptor, Random.Shared.Next());

            case "llm":
                var status = gateway.Status;
                if (status.State is not (LlmGatewayState.Ready or LlmGatewayState.Fake))
                {
                    throw new LlmUnavailableException(status.Detail);
                }

                var timeout = TimeSpan.FromSeconds(settings.MoveTimeoutSeconds);
                var (model, effort) = ModelVariant.Split(descriptor.ModelId);
                var conversation = await gateway.StartConversationAsync(
                    new LlmConversationOptions(model, RatedPromptBuilder.SystemPrompt, timeout, effort), ct);
                return new LlmChessPlayer(descriptor, conversation, settings.MaxAttemptsPerMove);

            default:
                throw new ArgumentException($"Unknown player kind '{descriptor.Kind}'. Use 'llm' or 'random'.", nameof(descriptor));
        }
    }
}
