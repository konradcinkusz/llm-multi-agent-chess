using Chess.Llm;
using Xunit;

namespace Chess.Llm.Tests;

public class ReplyReasoningTests
{
    [Fact]
    public void TheFinalMessagesReasoningWinsOverTheStreamedBlocks()
    {
        var chosen = ReplyReasoning.Choose("From the message.", ["From an event."]);

        Assert.Equal("From the message.", chosen);
    }

    [Fact]
    public void WithoutMessageReasoningTheStreamedBlocksAreJoinedInOrder()
    {
        var chosen = ReplyReasoning.Choose(null, ["First block.", "Second block."]);

        Assert.Equal("First block.\n\nSecond block.", chosen);
    }

    [Fact]
    public void BlankMessageReasoningFallsBackToTheStreamedBlocks()
    {
        var chosen = ReplyReasoning.Choose("   ", ["From an event."]);

        Assert.Equal("From an event.", chosen);
    }

    [Fact]
    public void BlankBlocksAreSkipped()
    {
        var chosen = ReplyReasoning.Choose(null, ["", " ", "Only this one."]);

        Assert.Equal("Only this one.", chosen);
    }

    [Fact]
    public void NoReasoningAtAllIsNull()
    {
        Assert.Null(ReplyReasoning.Choose(null, []));
        Assert.Null(ReplyReasoning.Choose("", ["  "]));
    }
}
