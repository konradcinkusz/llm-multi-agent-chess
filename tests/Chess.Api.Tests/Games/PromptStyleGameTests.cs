using Chess.Api.Games;
using Chess.Engine;
using Chess.Llm;
using Xunit;

namespace Chess.Api.Tests.Games;

public class PromptStyleGameTests
{
    [Fact]
    public async Task AThinkGameAgainstABoardGameFinishesAndItsAttemptsHoldTheirStyleText()
    {
        await using var harness = new GameHarness(GameHarness.RealPlayers(new FakeLlmGateway(seed: 9)));
        await harness.StartAsync();
        var gameId = await harness.SeedGameAsync(new("llm", "fake-a~think"), new("llm", "fake-b~board"));

        var events = await harness.PlayToEndAsync(gameId);

        Assert.IsType<GameFinished>(events[^1]);
        var attempts = await harness.LoadAttemptsAsync(gameId);
        Assert.NotEmpty(attempts);
        // White plays think (odd plies) and Black plays board (even plies).
        foreach (var attempt in attempts.Where(a => a.Ply % 2 == 1))
        {
            Assert.Contains("Answer with a short analysis", attempt.Prompt);
            Assert.Contains("\nMove: ", attempt.RawReply!);
        }

        foreach (var attempt in attempts.Where(a => a.Ply % 2 == 0))
        {
            Assert.Contains("Answer with one move in SAN only.", attempt.Prompt);
            Assert.DoesNotContain("Move:", attempt.RawReply!);
        }
    }
}
