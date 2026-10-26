using Microsoft.Extensions.Logging;

namespace Chess.Api.Requests.YouTube;

/// <summary>
/// Maps one YouTube chat message to a <see cref="ChatEvent"/> at the boundary (03 §15.5, P11). A Super Chat becomes a deposit
/// with its comment; a plain message starting with <c>!g</c> becomes a command that spends credit. Every other message is
/// ignored. Pure apart from the optional log line for a Super Chat that cannot be recorded.
/// </summary>
public static class YouTubeChatMapper
{
    public const string Source = "youtube";
    public const string CommandPrefix = "!g";

    private const string SuperChatType = "superChatEvent";
    private const string TextMessageType = "textMessageEvent";

    /// <param name="message">One item of a <c>liveChatMessages.list</c> page.</param>
    /// <param name="fallbackAt">The time to use when YouTube sends no <c>publishedAt</c>.</param>
    /// <param name="logger">Receives a warning for a Super Chat with no usable amount or currency. The message text is never logged.</param>
    /// <returns>The event, or null when the message is ignored or cannot be recorded.</returns>
    public static ChatEvent? Map(LiveChatMessage message, DateTimeOffset fallbackAt, ILogger? logger = null)
    {
        var snippet = message.Snippet;
        var at = snippet?.PublishedAt ?? fallbackAt;
        var payerId = message.AuthorDetails?.ChannelId ?? snippet?.AuthorChannelId;
        if (string.IsNullOrEmpty(payerId))
        {
            return null;
        }

        var payerDisplay = message.AuthorDetails?.DisplayName ?? string.Empty;
        switch (snippet?.Type)
        {
            case SuperChatType:
            {
                var details = snippet.SuperChatDetails;
                var currency = details?.Currency?.ToUpperInvariant();
                if (details?.AmountMicros is not > 0 || !IsCurrencyCode(currency))
                {
                    logger?.LogWarning("Super Chat {MessageId} has no usable amount or currency, so it is not recorded", message.Id);
                    return null;
                }

                return new ChatEvent(Source, message.Id, payerId, payerDisplay, details.AmountMicros, currency,
                    details.UserComment ?? string.Empty, at);
            }

            case TextMessageType:
            {
                var text = snippet.TextMessageDetails?.MessageText ?? string.Empty;
                return text.StartsWith(CommandPrefix, StringComparison.OrdinalIgnoreCase)
                    ? new ChatEvent(Source, message.Id, payerId, payerDisplay, null, null, text, at)
                    : null;
            }

            default:
                return null;
        }
    }

    private static bool IsCurrencyCode(string? code) => code is { Length: 3 } && code.All(char.IsAsciiLetterUpper);
}
