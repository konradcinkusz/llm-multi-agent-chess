using Chess.Api.Games;
using Chess.Api.Players;
using Xunit;

namespace Chess.Api.Tests.Games;

/// <summary>03 §10: every attempt is stored with its usage, and announced on the stream once it is stored.</summary>
public class AttemptRecordingTests
{
    private static readonly PlayerDescriptor Llm = new("llm", "fake-a");

    [Fact]
    public async Task EveryAttemptIsStoredWithItsUsageAndAnnouncedBeforeItsMoveOrRejection()
    {
        await using var harness = new GameHarness(
            new ScriptedPlayerFactory((descriptor, _) => new ScriptedPlayer(descriptor, ChooseAfterOneRejection)),
            maxPlies: 4);
        await harness.StartAsync();
        var gameId = await harness.SeedGameAsync(Llm, Llm);

        var events = await harness.PlayToEndAsync(gameId);

        var attempts = await harness.LoadAttemptsAsync(gameId);
        Assert.Equal(8, attempts.Count);
        var first = attempts.Single(a => a.Ply == 1 && a.Number == 1);
        Assert.Equal("garbage", first.RawReply);
        Assert.False(first.Legal);
        Assert.Equal(10, first.InputTokens);
        Assert.Equal(2, first.OutputTokens);
        Assert.Equal(5, first.LatencyMs);
        Assert.Equal("unparseable: \"garbage\" is not one move.", first.Error);
        var move = attempts.Single(a => a.Ply == 1 && a.Number == 2);
        Assert.True(move.Legal);
        Assert.Equal(12, move.InputTokens);
        Assert.Equal(3, move.OutputTokens);

        // Every stored attempt is announced once, with the same prompt, reply and usage.
        var recorded = events.OfType<AttemptRecorded>().ToList();
        Assert.Equal(attempts.Count, recorded.Count);
        foreach (var stored in attempts)
        {
            var announced = Assert.Single(recorded, r => r.Ply == stored.Ply && r.Attempt == stored.Number);
            Assert.Equal(stored.Prompt, announced.Prompt);
            Assert.Equal(stored.RawReply, announced.RawReply);
            Assert.Equal(stored.ParsedSan, announced.ParsedSan);
            Assert.Equal(stored.Legal, announced.Legal);
            Assert.Equal(stored.InputTokens, announced.InputTokens);
            Assert.Equal(stored.OutputTokens, announced.OutputTokens);
            Assert.Equal(stored.LatencyMs, announced.LatencyMs);
            Assert.Equal(stored.Ply % 2 == 1 ? "white" : "black", announced.Color);
            Assert.Equal("fake-a", announced.ModelId);
        }

        // Per ply: both attempts come first, then the rejection of the first, then the move.
        for (var ply = 1; ply <= 4; ply++)
        {
            var movePlayed = events.FindIndex(e => e is MovePlayed m && m.Ply == ply);
            var rejected = events.FindIndex(e => e is AttemptRejected r && r.Ply == ply);
            var recordedAt = events.Select((e, i) => (e, i))
                .Where(x => x.e is AttemptRecorded r && r.Ply == ply)
                .Select(x => x.i)
                .ToList();
            Assert.Equal(2, recordedAt.Count);
            Assert.All(recordedAt, i => Assert.True(i < rejected && rejected < movePlayed));
        }
    }

    [Fact]
    public async Task AForfeitStoresEveryRejectedAttemptBeforeTheForfeitIsAnnounced()
    {
        await using var harness = new GameHarness(
            new ScriptedPlayerFactory((descriptor, _) => new ScriptedPlayer(descriptor, (_, _) => Task.FromResult(Forfeit()))));
        await harness.StartAsync();
        var gameId = await harness.SeedGameAsync(Llm, Llm);

        var events = await harness.PlayToEndAsync(gameId);

        var attempts = await harness.LoadAttemptsAsync(gameId);
        Assert.Equal(3, attempts.Count);
        Assert.All(attempts, a => Assert.False(a.Legal));
        Assert.Equal(3, events.OfType<AttemptRecorded>().Count());
        var lastRecorded = events.FindLastIndex(e => e is AttemptRecorded);
        var firstRejected = events.FindIndex(e => e is AttemptRejected);
        var finished = events.FindIndex(e => e is GameFinished);
        Assert.True(lastRecorded < firstRejected);
        Assert.True(firstRejected < finished);
    }

    /// <summary>Attempt 1 is unparseable and carries usage; attempt 2 is the first legal move.</summary>
    private static Task<PlayerMove> ChooseAfterOneRejection(MoveRequest request, CancellationToken ct)
    {
        var san = request.LegalSan[0];
        var attempts = new[]
        {
            new MoveAttempt(1, $"prompt {request.Ply}.1", "garbage", null, false, TimeSpan.FromMilliseconds(5), 10, 2,
                "unparseable: \"garbage\" is not one move."),
            new MoveAttempt(2, $"prompt {request.Ply}.2", san, san, true, TimeSpan.FromMilliseconds(7), 12, 3, null),
        };
        return Task.FromResult(new PlayerMove(san, attempts, Forfeit: false));
    }

    private static PlayerMove Forfeit() => new(null,
    [
        new MoveAttempt(1, "prompt 1", "a", null, false, TimeSpan.FromMilliseconds(1), 5, 1, "unparseable: \"a\" is not one move."),
        new MoveAttempt(2, "prompt 2", "b", null, false, TimeSpan.FromMilliseconds(1), 5, 1, "unparseable: \"b\" is not one move."),
        new MoveAttempt(3, "prompt 3", "c", null, false, TimeSpan.FromMilliseconds(1), 5, 1, "unparseable: \"c\" is not one move."),
    ], Forfeit: true);
}
