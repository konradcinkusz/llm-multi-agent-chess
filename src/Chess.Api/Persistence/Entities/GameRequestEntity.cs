namespace Chess.Api.Persistence.Entities;

/// <summary>A viewer's request for a game on the stream (03 §15.4). <see cref="ExternalId"/> is unique: a message is recorded once.</summary>
public sealed class GameRequestEntity
{
    public Guid Id { get; set; }
    public string Source { get; set; } = "";
    public string ExternalId { get; set; } = "";
    public string PayerId { get; set; } = "";
    public string PayerDisplay { get; set; } = "";
    public string White { get; set; } = "";
    public string Black { get; set; } = "";

    /// <summary>The price in micro-dollars: 0 when no tier covered the matchup.</summary>
    public long PriceMicros { get; set; }

    public string Status { get; set; } = "";
    public string? Reason { get; set; }
    public Guid? GameId { get; set; }

    /// <summary>How many times the request was taken to play. A request is credited after its second failed attempt.</summary>
    public int Attempts { get; set; }

    public DateTimeOffset ReceivedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
}
