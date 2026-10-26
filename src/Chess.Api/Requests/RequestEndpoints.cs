using Chess.Api.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Chess.Api.Requests;

/// <summary>The request endpoints, relative to <c>/api</c> (03 §15.4).</summary>
public static class RequestEndpoints
{
    /// <summary>Largest amount the simulate form accepts, in the currency's units. A bound on a typing slip, not a price.</summary>
    private const double MaxSimulatedAmount = 1_000_000;

    public static IEndpointRouteBuilder MapRequestEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/requests");
        group.MapGet("/menu", GetMenuAsync);
        group.MapGet("/quote", GetQuoteAsync);
        group.MapGet("/queue", GetQueueAsync);
        group.MapPost("/simulate", SimulateAsync);
        group.MapPost("/{id:guid}/cancel", CancelAsync);
        return endpoints;
    }

    private static async Task<IResult> GetMenuAsync(RequestMenu menu, CancellationToken ct) =>
        TypedResults.Ok(await menu.MenuAsync(ct));

    private static async Task<IResult> GetQuoteAsync([FromQuery] string? white, [FromQuery] string? black, RequestMenu menu, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(white) || string.IsNullOrWhiteSpace(black))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["white"] = ["A white side is required, for example c1."],
                ["black"] = ["A black side is required, for example d3."],
            });
        }

        var outcome = await menu.QuoteAsync(white, black, ct);
        return outcome.Quote is { } quote
            ? TypedResults.Ok(quote)
            : TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Cannot quote this matchup",
                detail: outcome.Error);
    }

    private static async Task<IResult> GetQueueAsync(GameRequestQueue queue, CancellationToken ct) =>
        TypedResults.Ok(await queue.SnapshotAsync(ct));

    /// <summary>Runs a chat message through the same intake as YouTube. Only in Mock mode; elsewhere it answers 403.</summary>
    private static async Task<IResult> SimulateAsync(
        SimulateChatRequest body,
        IOptions<RequestOptions> options,
        IChatIntake intake,
        TimeProvider clock,
        CancellationToken ct)
    {
        if (options.Value.Mode != RequestMode.Mock)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Simulate is off",
                detail: "The simulate form runs only when Arena:Requests:Mode is Mock.");
        }

        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(body.PayerId))
        {
            errors["payerId"] = ["A payer id is required."];
        }

        if (string.IsNullOrWhiteSpace(body.Text))
        {
            errors["text"] = ["A message text is required."];
        }

        if (body.Amount is { } amount && (!double.IsFinite(amount) || amount <= 0 || amount > MaxSimulatedAmount))
        {
            errors["amount"] = ["A Super Chat amount must be a positive number of the currency's units."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var chatEvent = new ChatEvent(
            Source: "mock",
            ExternalId: $"mock:{Guid.NewGuid():N}",
            PayerId: body.PayerId,
            PayerDisplay: string.IsNullOrWhiteSpace(body.PayerDisplay) ? body.PayerId : body.PayerDisplay,
            AmountMicros: body.Amount is { } units ? Money.FromUnits(units) : null,
            Currency: body.Currency,
            Text: body.Text,
            At: clock.GetUtcNow());

        return TypedResults.Ok(await intake.AcceptAsync(chatEvent, ct));
    }

    private static async Task<IResult> CancelAsync(Guid id, GameRequestQueue queue, CancellationToken ct)
    {
        var outcome = await queue.CancelAsync(id, ct);
        if (!outcome.Found)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Request not found",
                detail: $"No request has the id {id}.");
        }

        return outcome.Request is { } request
            ? TypedResults.Ok(request)
            : TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Request cannot be cancelled",
                detail: outcome.Error);
    }
}
