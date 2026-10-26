using Chess.Api.Catalog;
using Chess.Api.Players.Prompts;
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
                var parts = ModelVariant.Parse(descriptor.ModelId);
                var style = PromptStyles.Find(parts.Style)
                    ?? throw new ArgumentException($"'{parts.Style}' is not a prompt style.", nameof(descriptor));
                var options = new LlmConversationOptions(parts.Model, style.SystemPrompt, timeout, parts.Effort);

                // A per-move style opens its conversation for each move (03 §14.2); classic opens one for the game.
                if (style.PerMoveConversation)
                {
                    return new LlmChessPlayer(descriptor, style, ct => gateway.StartConversationAsync(options, ct), settings.MaxAttemptsPerMove);
                }

                var conversation = await gateway.StartConversationAsync(options, ct);
                return new LlmChessPlayer(descriptor, conversation, settings.MaxAttemptsPerMove, style);

            default:
                throw new ArgumentException($"Unknown player kind '{descriptor.Kind}'. Use 'llm' or 'random'.", nameof(descriptor));
        }
    }
}
