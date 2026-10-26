namespace Chess.Api.Persistence.Entities;

/// <summary>
/// One line of a payer's ledger (03 §15.4). Lines are only ever added. A deposit is positive, a spend and a credit-back
/// are signed, and the balance is their sum. <see cref="ExternalId"/> is unique, so a repeated message adds nothing.
/// </summary>
public sealed class PayerLedgerEntity
{
    public Guid Id { get; set; }
    public string PayerId { get; set; } = "";

    /// <summary>One of <c>deposit</c>, <c>spend</c> or <c>credit</c>.</summary>
    public string Kind { get; set; } = "";

    /// <summary>The amount in micro-dollars: positive for a deposit or a credit, negative for a spend.</summary>
    public long AmountMicros { get; set; }

    public string ExternalId { get; set; } = "";
    public string? Currency { get; set; }
    public string? Reason { get; set; }
    public Guid? RequestId { get; set; }
    public DateTimeOffset At { get; set; }
}
