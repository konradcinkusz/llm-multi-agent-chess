using Chess.Api.Broadcast;
using Chess.Api.Persistence;
using Chess.Llm;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Chess.Api.Tests.Broadcast;

/// <summary>
/// The app on the InMemory provider with a named store, so that a test can seed schedules directly. Its gateway lists
/// premium multipliers (fake-a 3, fake-b unknown, fake-off disabled) that the fake gateway does not have.
/// </summary>
internal sealed class BroadcastApp(string databaseName) : WebApplicationFactory<Program>
{
    /// <summary>The controller the app uses in place of the runner's, so routes are tested without playing games.</summary>
    public FakeBroadcastController Controller { get; } = new(databaseName);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Database:Provider", "InMemory");
        builder.UseSetting("Database:InMemoryName", databaseName);
        builder.UseSetting("Llm:Provider", "Fake");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ILlmGateway>();
            services.AddSingleton<ILlmGateway>(new PricedGateway());
            services.RemoveAll<IBroadcastController>();
            services.AddSingleton<IBroadcastController>(Controller);
        });
    }

    public ArenaDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ArenaDbContext>().UseInMemoryDatabase(databaseName).Options);
}

/// <summary>A gateway that answers the model list only; the broadcast endpoints never start a conversation.</summary>
internal sealed class PricedGateway : ILlmGateway
{
    public LlmGatewayStatus Status { get; } = new(LlmGatewayState.Fake, "Test gateway with premium multipliers");

    public Task<IReadOnlyList<LlmModelInfo>> ListModelsAsync(CancellationToken ct)
    {
        IReadOnlyList<LlmModelInfo> models =
        [
            new LlmModelInfo("fake-a", "Fake A", 3, true, null, ["low", "medium", "high"], "medium"),
            new LlmModelInfo("fake-b", "Fake B", null, true),
            new LlmModelInfo("fake-off", "Fake Off", 1, false),
        ];
        return Task.FromResult(models);
    }

    public Task<ILlmConversation> StartConversationAsync(LlmConversationOptions options, CancellationToken ct) =>
        throw new NotSupportedException("The broadcast endpoints do not talk to models.");
}

/// <summary>
/// Stands in for the runner's controller. Each command returns <see cref="Result"/>; when that is Accepted the schedule
/// takes the status the command leads to (03 §9.1), as the real controller writes it. <see cref="Now"/> is what
/// <c>/now</c> returns.
/// </summary>
internal sealed class FakeBroadcastController(string databaseName) : IBroadcastController
{
    private readonly List<(string Command, Guid ScheduleId)> _calls = [];

    public IReadOnlyList<(string Command, Guid ScheduleId)> Calls => _calls;

    public BroadcastCommandResult Result { get; set; } = BroadcastCommandResult.Accepted;

    public NowNextDto Now { get; set; } = new("idle", null, [], Notice: null, EndsAt: null);

    public Task<BroadcastCommandResult> StartAsync(Guid scheduleId, CancellationToken ct) =>
        CommandAsync("start", scheduleId, ScheduleStatus.Running);

    public Task<BroadcastCommandResult> PauseAsync(Guid scheduleId, CancellationToken ct) =>
        CommandAsync("pause", scheduleId, ScheduleStatus.Paused);

    public Task<BroadcastCommandResult> ResumeAsync(Guid scheduleId, CancellationToken ct) =>
        CommandAsync("resume", scheduleId, ScheduleStatus.Running);

    public Task<BroadcastCommandResult> StopAsync(Guid scheduleId, CancellationToken ct) =>
        CommandAsync("stop", scheduleId, ScheduleStatus.Cancelled);

    public Task<NowNextDto> NowAsync(CancellationToken ct) => Task.FromResult(Now);

    private async Task<BroadcastCommandResult> CommandAsync(string command, Guid scheduleId, string target)
    {
        _calls.Add((command, scheduleId));
        if (Result != BroadcastCommandResult.Accepted)
        {
            return Result;
        }

        await using var db = new ArenaDbContext(new DbContextOptionsBuilder<ArenaDbContext>()
            .UseInMemoryDatabase(databaseName).Options);
        var schedule = await db.BroadcastSchedules.FirstOrDefaultAsync(s => s.Id == scheduleId);
        if (schedule is null)
        {
            return BroadcastCommandResult.NotFound;
        }

        schedule.Status = target;
        await db.SaveChangesAsync();
        return BroadcastCommandResult.Accepted;
    }
}

/// <summary>One app with its own store and client, so that each test starts from the database it names.</summary>
internal sealed class BroadcastHarness : IDisposable
{
    public BroadcastApp App { get; } = new(Guid.NewGuid().ToString());

    public HttpClient Client { get; }

    public BroadcastHarness()
    {
        Client = App.CreateClient();
    }

    public void Dispose()
    {
        Client.Dispose();
        App.Dispose();
    }
}
