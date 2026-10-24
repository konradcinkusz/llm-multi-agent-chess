using Chess.Api.Broadcast;
using Chess.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Chess.Api.Tests.Persistence;

public class BroadcastPersistenceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 25, 0, 0, 0, TimeSpan.Zero);

    private static ArenaDbContext NewContext(string databaseName) =>
        new(new DbContextOptionsBuilder<ArenaDbContext>().UseInMemoryDatabase(databaseName).Options);

    [Fact]
    public void Broadcast_tables_are_named_as_in_03_section_9_3()
    {
        using var context = NewContext(Guid.NewGuid().ToString());

        Assert.Equal("broadcast_schedules", context.Model.FindEntityType(typeof(BroadcastScheduleEntity))!.GetTableName());
        Assert.Equal("broadcast_slots", context.Model.FindEntityType(typeof(BroadcastSlotEntity))!.GetTableName());
    }

    [Fact]
    public async Task Schedule_round_trips_every_column_including_models()
    {
        var name = Guid.NewGuid().ToString();
        var schedule = new BroadcastScheduleEntity
        {
            Id = Guid.NewGuid(),
            Name = "Day one",
            Status = ScheduleStatus.Paused,
            StartsAt = T0,
            EndsAt = T0.AddHours(24),
            MoveDelayMs = 4000,
            GameGapSeconds = 20,
            Seed = 20261024,
            Models = ["fake-a", "fake-b", "random"],
            CreatedAt = T0.AddDays(-1),
            StartedAt = T0,
            FinishedAt = null,
        };
        await using (var write = NewContext(name))
        {
            write.BroadcastSchedules.Add(schedule);
            await write.SaveChangesAsync();
        }

        await using var read = NewContext(name);
        var loaded = await read.BroadcastSchedules.SingleAsync(s => s.Id == schedule.Id);

        Assert.Equal("Day one", loaded.Name);
        Assert.Equal(ScheduleStatus.Paused, loaded.Status);
        Assert.Equal(T0, loaded.StartsAt);
        Assert.Equal(T0.AddHours(24), loaded.EndsAt);
        Assert.Equal(4000, loaded.MoveDelayMs);
        Assert.Equal(20, loaded.GameGapSeconds);
        Assert.Equal(20261024, loaded.Seed);
        Assert.Equal(new[] { "fake-a", "fake-b", "random" }, loaded.Models);
        Assert.Equal(T0.AddDays(-1), loaded.CreatedAt);
        Assert.Equal(T0, loaded.StartedAt);
        Assert.Null(loaded.FinishedAt);
    }

    [Fact]
    public async Task Slot_round_trips_players_times_state_and_game_link()
    {
        var name = Guid.NewGuid().ToString();
        var scheduleId = Guid.NewGuid();
        var slot = new BroadcastSlotEntity
        {
            Id = Guid.NewGuid(),
            ScheduleId = scheduleId,
            Index = 7,
            WhiteKind = "llm",
            WhiteModel = "fake-a",
            BlackKind = "random",
            BlackModel = "random",
            PlannedStart = T0.AddMinutes(70),
            PlannedEnd = T0.AddMinutes(79),
            State = SlotState.Skipped,
            GameId = null,
            Attempts = 3,
            Note = "Copilot unavailable after 3 attempts",
        };
        await using (var write = NewContext(name))
        {
            write.BroadcastSlots.Add(slot);
            await write.SaveChangesAsync();
        }

        await using var read = NewContext(name);
        var loaded = await read.BroadcastSlots.SingleAsync(s => s.Id == slot.Id);

        Assert.Equal(scheduleId, loaded.ScheduleId);
        Assert.Equal(7, loaded.Index);
        Assert.Equal("llm", loaded.WhiteKind);
        Assert.Equal("fake-a", loaded.WhiteModel);
        Assert.Equal("random", loaded.BlackKind);
        Assert.Equal("random", loaded.BlackModel);
        Assert.Equal(T0.AddMinutes(70), loaded.PlannedStart);
        Assert.Equal(T0.AddMinutes(79), loaded.PlannedEnd);
        Assert.Equal(SlotState.Skipped, loaded.State);
        Assert.Null(loaded.GameId);
        Assert.Equal(3, loaded.Attempts);
        Assert.Equal("Copilot unavailable after 3 attempts", loaded.Note);
    }

    [Fact]
    public void Slot_index_is_unique_per_schedule()
    {
        using var context = NewContext(Guid.NewGuid().ToString());

        var index = context.Model.FindEntityType(typeof(BroadcastSlotEntity))!
            .GetIndexes()
            .Single(i => i.Properties.Select(p => p.Name).SequenceEqual(["ScheduleId", "Index"]));

        Assert.True(index.IsUnique);
    }
}
