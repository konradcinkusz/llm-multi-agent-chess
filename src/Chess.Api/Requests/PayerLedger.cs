using Chess.Api.Persistence;
using Chess.Api.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Chess.Api.Requests;

/// <summary>
/// A payer's credit (03 §15.1, §15.4). The ledger is append-only: a deposit, a spend and a credit-back are lines, and the
/// balance is their sum, read each time and never cached.
/// </summary>
public static class PayerLedger
{
    public const string DepositKind = "deposit";
    public const string SpendKind = "spend";
    public const string CreditKind = "credit";

    /// <summary>The external id of the spend that pays for the request a message made.</summary>
    public static string SpendExternalId(string messageExternalId) => $"{messageExternalId}:spend";

    /// <summary>The external id of the credit-back for a request. One per request, so a request is credited once.</summary>
    public static string CreditExternalId(Guid requestId) => $"credit:{requestId:D}";

    /// <summary>The balance in micro-units: the sum of the payer's lines.</summary>
    public static Task<long> BalanceMicrosAsync(ArenaDbContext db, string payerId, CancellationToken ct) =>
        db.PayerLedger.AsNoTracking()
            .Where(line => line.PayerId == payerId)
            .SumAsync(line => line.AmountMicros, ct);

    /// <summary>True when a line or a request already carries the external id: the message was recorded before.</summary>
    public static async Task<bool> IsRecordedAsync(ArenaDbContext db, string externalId, CancellationToken ct) =>
        await db.PayerLedger.AsNoTracking().AnyAsync(line => line.ExternalId == externalId, ct)
        || await db.GameRequests.AsNoTracking().AnyAsync(request => request.ExternalId == externalId, ct);

    /// <summary>Credits a request's price back to the payer, once. The request's status decides when this is called.</summary>
    public static async Task CreditAsync(ArenaDbContext db, GameRequestEntity request, DateTimeOffset at, CancellationToken ct)
    {
        if (request.PriceMicros <= 0)
        {
            return;
        }

        var externalId = CreditExternalId(request.Id);
        if (await db.PayerLedger.AsNoTracking().AnyAsync(line => line.ExternalId == externalId, ct))
        {
            return;
        }

        db.PayerLedger.Add(new PayerLedgerEntity
        {
            Id = Guid.NewGuid(),
            PayerId = request.PayerId,
            Kind = CreditKind,
            AmountMicros = request.PriceMicros,
            ExternalId = externalId,
            RequestId = request.Id,
            Reason = "credited back",
            At = at,
        });
    }
}
