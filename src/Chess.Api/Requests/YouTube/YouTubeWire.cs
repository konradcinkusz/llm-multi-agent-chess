namespace Chess.Api.Requests.YouTube;

// The fields of the YouTube Data API v3 responses that the intake reads (liveBroadcasts.list and liveChatMessages.list).
// Everything else in a response is ignored. Names are camelCase on the wire, as JsonSerializerDefaults.Web expects.

/// <summary>The answer of <c>liveBroadcasts.list</c>.</summary>
public sealed class LiveBroadcastList
{
    public List<LiveBroadcast> Items { get; set; } = [];
}

/// <summary>One broadcast. Only the snippet's live chat id is read.</summary>
public sealed class LiveBroadcast
{
    public BroadcastSnippet? Snippet { get; set; }
}

/// <summary>The part of a broadcast's snippet that names its chat.</summary>
public sealed class BroadcastSnippet
{
    public string? LiveChatId { get; set; }
}

/// <summary>The answer of <c>liveChatMessages.list</c>: one page, the token of the next, and how long to wait before asking.</summary>
public sealed class LiveChatMessagesResponse
{
    public string? NextPageToken { get; set; }

    /// <summary>YouTube's suggested gap in milliseconds. The intake never polls faster than its own minimum.</summary>
    public long? PollingIntervalMillis { get; set; }

    public List<LiveChatMessage> Items { get; set; } = [];
}

/// <summary>One chat message.</summary>
public sealed class LiveChatMessage
{
    public string Id { get; set; } = string.Empty;

    public ChatMessageSnippet? Snippet { get; set; }

    public ChatAuthor? AuthorDetails { get; set; }
}

/// <summary>The snippet of a chat message. Only the fields of the two kinds the intake maps are read.</summary>
public sealed class ChatMessageSnippet
{
    /// <summary><c>superChatEvent</c>, <c>textMessageEvent</c> or another kind the intake ignores.</summary>
    public string? Type { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }

    public string? AuthorChannelId { get; set; }

    public TextMessageDetails? TextMessageDetails { get; set; }

    public SuperChatDetails? SuperChatDetails { get; set; }
}

/// <summary>A plain text message.</summary>
public sealed class TextMessageDetails
{
    public string? MessageText { get; set; }
}

/// <summary>A Super Chat. <c>amountMicros</c> is a string of millionths of the currency on the wire.</summary>
public sealed class SuperChatDetails
{
    public long? AmountMicros { get; set; }

    public string? Currency { get; set; }

    public string? UserComment { get; set; }
}

/// <summary>Who sent the message.</summary>
public sealed class ChatAuthor
{
    public string? ChannelId { get; set; }

    public string? DisplayName { get; set; }
}
