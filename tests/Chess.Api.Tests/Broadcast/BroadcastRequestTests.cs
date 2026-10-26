using Chess.Api.Broadcast;
using Chess.Api.Contracts;
using Chess.Api.Catalog;
using Chess.Api.Games;
using Chess.Api.Persistence.Entities;
using Chess.Api.Requests;
using Chess.Api.Tests.Games;
using Chess.Llm;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Chess.Api.Tests.Broadcast;

/// <summary>The broadcast runner plays paid requests (03 §15.1, §15.2): the order against slots, the budget, refusal and failure.</summary>
public class BroadcastRequestTests
{
    private const string CustomA = "custom-a";
    private const string CustomB = "custom-b";
    private const int MoveDelayMs = 1000;
    private const int GapSeconds = 5;

    private static readonly PlayerSpec RandomPlayer = RunnerHarness.RandomPlayer;

    private static CustomModelOptions Priced(string id) => new()
    {
        Id = id,
        ProviderType = "openai",
        BaseUrl = "https://models.invalid/v1",
        Model = id,
        UsdPerMillionInput = 1,
        UsdPerMillionOutput = 2,
    };

    /// <summary>Two priced custom models that play, and the one that never answers, which the failure test needs.</summary>
    private static RunnerHarness NewHarness() =>
        new(Priced(CustomA), Priced(CustomB), Priced(BrokenOrRealPlayers.BrokenModel));

    /// <summary>Queues a request as the intake would, with its price in US dollars. The queue derives the budget from it.</summary>
    private static async Task<Guid> QueueAsync(RunnerHarness h, string white, string black, double priceUsd)
    {
        var id = Guid.NewGuid();
        await using var db = await h.Factory.CreateDbContextAsync();
        db.GameRequests.Add(new GameRequestEntity
        {
            Id = id,
            Source = "mock",
            ExternalId = $"mock:{id:N}",
            PayerId = "viewer",
            PayerDisplay = "Viewer",
            White = white,
            Black = black,
            PriceMicros = Money.FromUnits(priceUsd),
            Status = RequestStatus.Queued,
            ReceivedAt = RunnerHarness.Start.AddMinutes(-5),
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task<GameRequestEntity> RequestAsync(RunnerHarness h, Guid id)
    {
        await using var db = await h.Factory.CreateDbContextAsync();
        return await db.GameRequests.AsNoTracking().SingleAsync(r => r.Id == id);
    }

    /// <summary>The amount credited back to the payer for a request, in micro-units, or 0 when it was not credited.</summary>
    private static async Task<long> CreditAsync(RunnerHarness h, Guid requestId)
    {
        await using var db = await h.Factory.CreateDbContextAsync();
        return await db.PayerLedger.AsNoTracking()
            .Where(l => l.RequestId == requestId && l.Kind == PayerLedger.CreditKind)
            .SumAsync(l => l.AmountMicros);
    }

    [Fact]
    public async Task A_queued_request_plays_to_the_end_with_no_schedule_running()
    {
        await using var h = NewHarness();
        var requestId = await QueueAsync(h, CustomA, CustomB, 1000);
        await h.StartRunnerAsync();

        await h.Clock.RunUntilAsync(async () => (await RequestAsync(h, requestId)).Status == RequestStatus.Played,
            "the request to be played");

        var request = await RequestAsync(h, requestId);
        Assert.NotNull(request.GameId);
        Assert.NotNull(request.FinishedAt);
        var game = await h.GameAsync(request.GameId!.Value);
        Assert.True(game.Rated);
        Assert.Equal(CustomA, game.WhiteModel);
        Assert.Equal(CustomB, game.BlackModel);
        Assert.NotEqual("*", game.Result);
        Assert.NotEqual(GameTermination.Aborted.Termination, game.Termination);
        Assert.Equal(0, await CreditAsync(h, requestId));
    }

    [Fact]
    public async Task A_tiny_budget_stops_the_game_as_budget_and_adjudicates_it()
    {
        await using var h = NewHarness();
        // One micro-dollar of price is 0.7 micro-dollars of budget, and the fake's first reply costs more than that.
        var requestId = await QueueAsync(h, CustomA, CustomB, 0.000001);
        await h.StartRunnerAsync();

        await h.Clock.RunUntilAsync(async () => (await RequestAsync(h, requestId)).Status == RequestStatus.Played,
            "the request to be played");

        var game = await h.GameAsync((await RequestAsync(h, requestId)).GameId!.Value);
        Assert.Equal(GameTermination.BudgetReached, game.Termination);
        Assert.Equal(1, game.PlyCount);
        Assert.NotEqual("*", game.Result);
    }

    [Fact]
    public async Task A_copilot_model_in_a_request_is_refused_and_credited_without_a_game()
    {
        await using var h = NewHarness();
        var requestId = await QueueAsync(h, "copilot-model", CustomB, 10);
        await h.StartRunnerAsync();

        await h.Clock.RunUntilAsync(async () => (await RequestAsync(h, requestId)).Status == RequestStatus.Failed,
            "the request to be refused");

        var request = await RequestAsync(h, requestId);
        Assert.Equal(BroadcastRequests.NotCustomReason, request.Reason);
        Assert.Null(request.GameId);
        Assert.Empty(await h.GamesAsync());
        Assert.Equal(Money.FromUnits(10), await CreditAsync(h, requestId));
    }

    [Fact]
    public async Task A_request_whose_game_gets_no_reply_is_requeued_once_then_credited()
    {
        await using var h = NewHarness();
        var requestId = await QueueAsync(h, BrokenOrRealPlayers.BrokenModel, CustomB, 10);
        await h.StartRunnerAsync();

        await h.Clock.RunUntilAsync(async () => (await RequestAsync(h, requestId)).Status == RequestStatus.Failed,
            "the request to be credited");

        var request = await RequestAsync(h, requestId);
        // Taken twice: a requeue after the first failure, then the credit.
        Assert.Equal(2, request.Attempts);
        var games = (await h.GamesAsync()).Where(g => g.WhiteModel == BrokenOrRealPlayers.BrokenModel).ToList();
        Assert.Equal(2, games.Count);
        Assert.All(games, g => Assert.Equal(GameTermination.Aborted.Termination, g.Termination));
        Assert.Equal(Money.FromUnits(10), await CreditAsync(h, requestId));
    }

    [Fact]
    public async Task A_queued_request_goes_before_the_next_planned_slot()
    {
        await using var h = NewHarness();
        var scheduleId = await h.SeedScheduleAsync(ScheduleStatus.Running, MoveDelayMs, GapSeconds, (RandomPlayer, RandomPlayer));
        var requestId = await QueueAsync(h, CustomA, CustomB, 1000);
        await h.StartRunnerAsync();

        await h.Clock.RunUntilAsync(async () => (await h.ScheduleAsync(scheduleId)).Status == ScheduleStatus.Finished,
            "the schedule to finish");

        var games = await h.GamesAsync();
        Assert.Equal(2, games.Count);
        Assert.Equal((await RequestAsync(h, requestId)).GameId, games[0].Id);
        Assert.Equal((await h.SlotsAsync(scheduleId)).Single().GameId, games[1].Id);
    }

    [Fact]
    public async Task A_request_that_arrives_during_a_slot_does_not_interrupt_it()
    {
        await using var h = NewHarness();
        var scheduleId = await h.SeedScheduleAsync(ScheduleStatus.Running, MoveDelayMs, GapSeconds, (RandomPlayer, RandomPlayer));
        await h.StartRunnerAsync();
        await h.Clock.RunUntilAsync(async () => (await h.GamesAsync()) is [{ PlyCount: >= 3 }], "three plies of the slot's game");

        var requestId = await QueueAsync(h, CustomA, CustomB, 1000);
        await h.Clock.RunUntilAsync(async () => (await h.ScheduleAsync(scheduleId)).Status == ScheduleStatus.Finished,
            "the schedule to finish");
        await h.Clock.RunUntilAsync(async () => (await RequestAsync(h, requestId)).Status == RequestStatus.Played,
            "the request to be played");

        var games = await h.GamesAsync();
        Assert.Equal(2, games.Count);
        var slotGame = games[0];
        Assert.Equal((await h.SlotsAsync(scheduleId)).Single().GameId, slotGame.Id);
        Assert.Equal(SlotState.Finished, (await h.SlotsAsync(scheduleId)).Single().State);
        Assert.NotEqual(GameTermination.Aborted.Termination, slotGame.Termination);
        Assert.NotEqual("*", slotGame.Result);
        Assert.True(games[1].CreatedAt >= slotGame.FinishedAt!.Value, "the request's game waits for the slot's game to finish");
    }
}
