using Chess.Api.Contracts;
using Chess.Api.Persistence;
using Chess.Api.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Chess.Api.Requests;

/// <summary>
/// Turns one chat message into credit and requests (03 §15.1, §15.3). A Super Chat is a deposit first; a message with a command
/// is then quoted and either queued (its price spent from the balance) or rejected (the credit stays). Everything a message
/// changes is saved in one call, so a message is either fully recorded or not at all.
/// </summary>
public sealed class ChatIntake(IDbContextFactory<ArenaDbContext> factory, RequestMenu menu, IOptions<RequestOptions> options)
    : IChatIntake
{
    public async Task<IntakeResult> AcceptAsync(ChatEvent chatEvent, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(chatEvent.ExternalId))
        {
            throw new ArgumentException("A chat message needs its platform id.", nameof(chatEvent));
        }

        if (string.IsNullOrWhiteSpace(chatEvent.PayerId))
        {
            throw new ArgumentException("A chat message needs its sender's id.", nameof(chatEvent));
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        if (await PayerLedger.IsRecordedAsync(db, chatEvent.ExternalId, ct))
        {
            return new IntakeResult(Duplicate: true, DepositUsd: null, Request: null);
        }

        var depositMicros = AddDeposit(db, chatEvent);
        var deposit = depositMicros is null ? (double?)null : Money.ToUnits(depositMicros.Value);
        var parse = RequestCommandParser.Parse(chatEvent.Text);
        if (parse is null)
        {
            await db.SaveChangesAsync(ct);
            return new IntakeResult(Duplicate: false, DepositUsd: deposit, Request: null);
        }

        var decision = await DecideAsync(db, chatEvent, parse, depositMicros ?? 0, ct);
        var request = new GameRequestEntity
        {
            Id = Guid.NewGuid(),
            Source = chatEvent.Source,
            ExternalId = chatEvent.ExternalId,
            PayerId = chatEvent.PayerId,
            PayerDisplay = chatEvent.PayerDisplay,
            White = decision.White,
            Black = decision.Black,
            PriceMicros = decision.PriceMicros,
            Status = decision.Status,
            Reason = decision.Reason,
            ReceivedAt = chatEvent.At,
        };

        db.GameRequests.Add(request);
        if (decision.Status == RequestStatus.Queued)
        {
            db.PayerLedger.Add(new PayerLedgerEntity
            {
                Id = Guid.NewGuid(),
                PayerId = chatEvent.PayerId,
                Kind = PayerLedger.SpendKind,
                AmountMicros = -decision.PriceMicros,
                ExternalId = PayerLedger.SpendExternalId(chatEvent.ExternalId),
                RequestId = request.Id,
                Reason = "game requested",
                At = chatEvent.At,
            });
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // The unique external id refused a second copy: another delivery of this message was recorded first.
            if (await PayerLedger.IsRecordedAsync(db, chatEvent.ExternalId, ct))
            {
                return new IntakeResult(Duplicate: true, DepositUsd: null, Request: null);
            }

            throw;
        }

        var position = request.Status == RequestStatus.Queued
            ? await db.GameRequests.AsNoTracking().CountAsync(r => r.Status == RequestStatus.Queued, ct)
            : (int?)null;

        return new IntakeResult(Duplicate: false, DepositUsd: deposit, Request: GameRequestQueue.Map(request, position));
    }

    /// <summary>
    /// Records a Super Chat as a deposit, gross and in USD micro-units. An amount that cannot be valued is recorded at 0 with its reason.
    /// </summary>
    /// <returns>The deposit in micro-USD, or null for an unpaid message.</returns>
    private long? AddDeposit(ArenaDbContext db, ChatEvent chatEvent)
    {
        if (chatEvent.AmountMicros is null && chatEvent.Currency is null)
        {
            return null;
        }

        var value = RequestPricing.Deposit(chatEvent.AmountMicros, chatEvent.Currency, options.Value.UsdPerUnit);
        db.PayerLedger.Add(new PayerLedgerEntity
        {
            Id = Guid.NewGuid(),
            PayerId = chatEvent.PayerId,
            Kind = PayerLedger.DepositKind,
            AmountMicros = value.UsdMicros,
            ExternalId = chatEvent.ExternalId,
            Currency = chatEvent.Currency?.Trim().ToUpperInvariant(),
            Reason = value.Reason,
            At = chatEvent.At,
        });

        return value.UsdMicros;
    }

    /// <summary>Quotes a command and decides whether it is queued. The balance includes this message's own deposit.</summary>
    private async Task<Decision> DecideAsync(ArenaDbContext db, ChatEvent chatEvent, CommandParse parse, long depositMicros, CancellationToken ct)
    {
        if (parse.Command is not { } command)
        {
            return Decision.Rejected(string.Empty, string.Empty, parse.Error!);
        }

        var white = ToPlayerSide(command.WhiteCode, command.WhiteStyle);
        var black = ToPlayerSide(command.BlackCode, command.BlackStyle);
        var quoted = await menu.QuoteAsync(white, black, ct);
        if (quoted.Quote is not { } quote)
        {
            return Decision.Rejected(string.Empty, string.Empty, quoted.Error!);
        }

        if (quote.PriceUsd is not { } price)
        {
            var largest = options.Value.Tiers.Max();
            return Decision.Rejected(quote.White, quote.Black,
                $"Above the largest tier (${largest:0.##}), so not offered.");
        }

        var priceMicros = Money.FromUnits(price);
        var balance = await PayerLedger.BalanceMicrosAsync(db, chatEvent.PayerId, ct) + depositMicros;
        if (balance < priceMicros)
        {
            return new Decision(RequestStatus.Rejected, quote.White, quote.Black, 0,
                $"Credit ${Money.ToUnits(balance):0.00} does not cover ${price:0.00}. The credit is kept.");
        }

        return new Decision(RequestStatus.Queued, quote.White, quote.Black, priceMicros, null);
    }

    private static string ToPlayerSide(string code, string? style) => style is null ? code : $"{code}~{style}";

    /// <summary>What a message's command became: its status, the sides as the stream shows them, the price and the reason.</summary>
    private sealed record Decision(string Status, string White, string Black, long PriceMicros, string? Reason)
    {
        public static Decision Rejected(string white, string black, string reason) =>
            new(RequestStatus.Rejected, white, black, 0, reason);
    }
}
