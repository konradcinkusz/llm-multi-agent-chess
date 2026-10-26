namespace Chess.Api.Requests;

/// <summary>
/// One chat message as the arena sees it, whatever platform it came from (03 §15.4). The YouTube intake maps its
/// messages onto this at the boundary, so nothing past it knows YouTube's vocabulary (P11).
/// </summary>
/// <param name="Source">Where it came from: <c>youtube</c> or <c>mock</c>.</param>
/// <param name="ExternalId">The platform's id of the message. A message is recorded once by it.</param>
/// <param name="PayerId">The sender's stable id on the platform (the YouTube channel id). Credit belongs to it.</param>
/// <param name="PayerDisplay">The sender's display name, shown on the stream next to a request.</param>
/// <param name="AmountMicros">The paid amount in millionths of the currency for a paid message, else null.</param>
/// <param name="Currency">The ISO 4217 code of a paid message, else null.</param>
/// <param name="Text">The message text. Never shown on the stream; only the parsed command is.</param>
public sealed record ChatEvent(string Source, string ExternalId, string PayerId, string PayerDisplay,
    long? AmountMicros, string? Currency, string Text, DateTimeOffset At);

/// <summary>What the intake did with one chat event.</summary>
/// <param name="Duplicate">True when the event was already recorded; nothing changed.</param>
/// <param name="DepositUsd">The gross USD added to the payer's balance, or null for an unpaid message.</param>
/// <param name="Request">The request the event's command created, or null when it carried none.</param>
public sealed record IntakeResult(bool Duplicate, double? DepositUsd, Contracts.GameRequestDto? Request);

/// <summary>The one place where a chat event becomes credit and requests (03 §15.1). Idempotent by <see cref="ChatEvent.ExternalId"/>.</summary>
public interface IChatIntake
{
    Task<IntakeResult> AcceptAsync(ChatEvent chatEvent, CancellationToken ct);
}

/// <summary>A queued request handed to the broadcast runner.</summary>
public sealed record QueuedRequest(Guid Id, string White, string Black, double PriceUsd, double BudgetUsd, string PayerDisplay);

/// <summary>The request queue as the broadcast runner uses it (03 §15.1, §15.4).</summary>
public interface IGameRequestQueue
{
    /// <summary>Takes the oldest queued request and marks it <c>playing</c>, or returns null when there is none.</summary>
    Task<QueuedRequest?> TakeNextAsync(CancellationToken ct);

    /// <summary>Links the request to the game it plays.</summary>
    Task StartedAsync(Guid requestId, Guid gameId, CancellationToken ct);

    /// <summary>Marks the request <c>played</c>.</summary>
    Task PlayedAsync(Guid requestId, CancellationToken ct);

    /// <summary>The game could not be played for a reason on the arena's side: requeued once, then credited.</summary>
    Task FailedAsync(Guid requestId, string reason, CancellationToken ct);
}

/// <summary>The status vocabulary of a request (03 §15.4).</summary>
public static class RequestStatus
{
    public const string Received = "received";
    public const string Queued = "queued";
    public const string Playing = "playing";
    public const string Played = "played";
    public const string Rejected = "rejected";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
}
