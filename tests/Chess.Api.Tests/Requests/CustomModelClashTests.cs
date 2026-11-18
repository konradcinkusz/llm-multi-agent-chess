using Chess.Api.Broadcast;
using Chess.Api.Catalog;
using Chess.Api.Contracts;
using Chess.Api.Persistence;
using Chess.Api.Persistence.Entities;
using Chess.Api.Requests;
using Chess.Api.Tournaments;
using Chess.Llm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Chess.Api.Tests.Requests;

/// <summary>
/// A custom model whose id a Copilot model also uses is dropped by the Copilot gateway: the id resolves to the Copilot model and
/// its game would be billed to the subscription. With RequireApiKeyModels on, such a model is never quoted, never played for a
/// viewer, and stops start-up (03 §15.2, docs/REQUESTS.md).
/// </summary>
public class CustomModelClashTests
{
    private const string Clash = "gpt-clash";
    private const string Own = "own-model";

    /// <summary>A gateway whose listing has the Copilot model <see cref="Clash"/> (not custom) and the custom <see cref="Own"/>.</summary>
    private sealed class ClashGateway(LlmGatewayState state = LlmGatewayState.Ready) : ILlmGateway
    {
        public LlmGatewayStatus Status { get; } = new(state, "test");

        public Task<IReadOnlyList<LlmModelInfo>> ListModelsAsync(CancellationToken ct)
        {
            var prices = new LlmTokenPrices(1_000_000, 1, 2, null);
            IReadOnlyList<LlmModelInfo> models =
            [
                new(Clash, "Copilot clash", null, true, prices),
                new(Own, "Own", null, true, prices, Custom: true),
            ];
            return Task.FromResult(models);
        }

        public Task<ILlmConversation> StartConversationAsync(LlmConversationOptions options, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private static ArenaOptions ArenaWith(params string[] ids) =>
        new() { CustomModels = ids.Select(id => new CustomModelOptions { Id = id }).ToList() };

    private static ModelCatalog CatalogOf(ILlmGateway gateway) =>
        new(gateway, new MemoryCache(new MemoryCacheOptions()), Options.Create(new CatalogOptions()));

    private static RequestOptions MenuOf(params string[] playerIds) => new()
    {
        Mode = RequestMode.Mock,
        RequireApiKeyModels = true,
        Menu = playerIds.Select((id, i) => new RequestMenuEntry { Code = $"m{i}", PlayerId = id }).ToList(),
    };

    [Fact]
    public async Task A_clashing_id_is_not_quoted_but_a_real_custom_model_is()
    {
        var listing = await CatalogOf(new ClashGateway()).GetModelsAsync(default);
        var options = MenuOf(Clash, Own);

        var clash = RequestMenu.PriceSide("m0", listing, new Dictionary<string, ModelTokenAverage>(), options, 0.01);
        var own = RequestMenu.PriceSide("m1", listing, new Dictionary<string, ModelTokenAverage>(), options, 0.01);

        Assert.Null(clash.Usd);
        Assert.Contains("bring-your-own-key", clash.Error);
        Assert.Null(own.Error);
        Assert.NotNull(own.Usd);
    }

    [Fact]
    public async Task Without_the_requirement_the_copilot_model_is_quoted()
    {
        var listing = await CatalogOf(new ClashGateway()).GetModelsAsync(default);
        var options = MenuOf(Clash);
        options.RequireApiKeyModels = false;

        var side = RequestMenu.PriceSide("m0", listing, new Dictionary<string, ModelTokenAverage>(), options, 0.01);

        Assert.Null(side.Error);
    }

    [Fact]
    public async Task Start_up_fails_with_a_clear_message_when_a_menu_model_clashes()
    {
        var check = new CustomModelClashCheck(CatalogOf(new ClashGateway()), Options.Create(MenuOf(Clash)), Options.Create(ArenaWith(Clash)));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => check.StartAsync(default));

        Assert.Contains($"'{Clash}'", error.Message);
        Assert.Contains("Copilot subscription", error.Message);
    }

    [Fact]
    public async Task Start_up_passes_without_a_clash_with_the_requirement_off_or_with_the_mode_off()
    {
        var gateway = new ClashGateway();

        await new CustomModelClashCheck(CatalogOf(gateway), Options.Create(MenuOf(Own)), Options.Create(ArenaWith(Own, Clash)))
            .StartAsync(default);

        var notRequired = MenuOf(Clash);
        notRequired.RequireApiKeyModels = false;
        await new CustomModelClashCheck(CatalogOf(gateway), Options.Create(notRequired), Options.Create(ArenaWith(Clash))).StartAsync(default);

        var off = MenuOf(Clash);
        off.Mode = RequestMode.Off;
        await new CustomModelClashCheck(CatalogOf(gateway), Options.Create(off), Options.Create(ArenaWith(Clash))).StartAsync(default);
    }

    [Fact]
    public async Task Start_up_passes_when_the_gateway_is_not_ready_because_nothing_can_be_told_apart()
    {
        var gateway = new ClashGateway(LlmGatewayState.Unavailable);

        await new CustomModelClashCheck(CatalogOf(gateway), Options.Create(MenuOf(Clash)), Options.Create(ArenaWith(Clash))).StartAsync(default);
    }

    [Fact]
    public async Task A_request_with_a_clashing_side_is_refused_and_credited_without_a_game()
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<ArenaDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IDbContextFactory<ArenaDbContext>>();
        var clock = TimeProvider.System;
        var requestOptions = Options.Create(new RequestOptions());
        var queue = new GameRequestQueue(factory, requestOptions, clock);
        var arena = Options.Create(ArenaWith(Clash, Own));
        var broadcast = new BroadcastRequests(factory, queue, CatalogOf(new ClashGateway()), arena, requestOptions,
            Options.Create(new CatalogOptions()), clock);

        var id = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.GameRequests.Add(new GameRequestEntity
            {
                Id = id, Source = "mock", ExternalId = $"mock:{id:N}", PayerId = "viewer", PayerDisplay = "Viewer",
                White = Clash, Black = Own, PriceMicros = Money.FromUnits(5), Status = RequestStatus.Queued,
                ReceivedAt = DateTimeOffset.UnixEpoch,
            });
            await db.SaveChangesAsync();
        }

        var taken = await queue.TakeNextAsync(default);
        var game = await broadcast.StartAsync(taken!, default);

        Assert.Null(game);
        await using var check = await factory.CreateDbContextAsync();
        var stored = await check.GameRequests.AsNoTracking().SingleAsync(r => r.Id == id);
        Assert.Equal(RequestStatus.Failed, stored.Status);
        Assert.Equal(BroadcastRequests.NotCustomReason, stored.Reason);
        Assert.Null(stored.GameId);
        Assert.Empty(check.Games);
        Assert.Equal(Money.FromUnits(5), check.PayerLedger.Where(l => l.RequestId == id && l.Kind == PayerLedger.CreditKind).Sum(l => l.AmountMicros));
    }

    [Fact]
    public async Task A_request_is_requeued_not_refused_while_the_gateway_is_not_ready()
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<ArenaDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IDbContextFactory<ArenaDbContext>>();
        var requestOptions = Options.Create(new RequestOptions());
        var queue = new GameRequestQueue(factory, requestOptions, TimeProvider.System);
        var broadcast = new BroadcastRequests(factory, queue, CatalogOf(new ClashGateway(LlmGatewayState.Unavailable)),
            Options.Create(ArenaWith(Own)), requestOptions, Options.Create(new CatalogOptions()), TimeProvider.System);

        await Assert.ThrowsAsync<CatalogNotReadyException>(() =>
            broadcast.StartAsync(new QueuedRequest(Guid.NewGuid(), Own, Own, 1, 0.7, "Viewer"), default));
    }

    [Fact]
    public async Task Releasing_a_request_gives_the_attempt_back_and_keeps_its_place()
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<ArenaDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IDbContextFactory<ArenaDbContext>>();
        var queue = new GameRequestQueue(factory, Options.Create(new RequestOptions()), TimeProvider.System);
        var id = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.GameRequests.Add(new GameRequestEntity
            {
                Id = id, Source = "mock", ExternalId = $"mock:{id:N}", PayerId = "viewer", PayerDisplay = "Viewer",
                White = Own, Black = Own, PriceMicros = Money.FromUnits(5), Status = RequestStatus.Queued,
                ReceivedAt = DateTimeOffset.UnixEpoch,
            });
            await db.SaveChangesAsync();
        }

        for (var i = 0; i < 4; i++)
        {
            Assert.NotNull(await queue.TakeNextAsync(default));
            await queue.ReleaseAsync(id, "waiting", default);
        }

        await using var check = await factory.CreateDbContextAsync();
        var stored = await check.GameRequests.AsNoTracking().SingleAsync(r => r.Id == id);
        Assert.Equal(RequestStatus.Queued, stored.Status);
        Assert.Equal(0, stored.Attempts);
        Assert.Empty(check.PayerLedger);
    }
}
