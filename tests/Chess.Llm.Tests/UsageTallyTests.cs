using Chess.Llm;
using Xunit;

namespace Chess.Llm.Tests;

public class UsageTallyTests
{
    [Fact]
    public void SumsEveryUsageEventOfOneReply()
    {
        var tally = new UsageTally();

        tally.Add(inputTokens: 100, outputTokens: 20, reasoningTokens: 8, cachedInputTokens: 60, totalNanoAiu: 2_000_000_000, creditsUnavailable: false);
        tally.Add(inputTokens: 50, outputTokens: 5, reasoningTokens: 2, cachedInputTokens: 10, totalNanoAiu: 500_000_000, creditsUnavailable: false);

        var usage = tally.Snapshot();
        Assert.Equal(150, usage.InputTokens);
        Assert.Equal(25, usage.OutputTokens);
        Assert.Equal(10, usage.ReasoningTokens);
        Assert.Equal(70, usage.CachedInputTokens);
        Assert.Equal(2.5, usage.AiCredits);
    }

    [Fact]
    public void MissingFieldsAddNothingAndAFieldNoEventReportedIsNull()
    {
        var tally = new UsageTally();

        tally.Add(inputTokens: null, outputTokens: 7, reasoningTokens: null, cachedInputTokens: null, totalNanoAiu: null, creditsUnavailable: false);
        tally.Add(inputTokens: 40, outputTokens: null, reasoningTokens: null, cachedInputTokens: null, totalNanoAiu: null, creditsUnavailable: false);

        var usage = tally.Snapshot();
        Assert.Equal(40, usage.InputTokens);
        Assert.Equal(7, usage.OutputTokens);
        Assert.Null(usage.ReasoningTokens);
        Assert.Null(usage.CachedInputTokens);
        Assert.Null(usage.AiCredits);
    }

    [Fact]
    public void AReplyWithNoUsageEventAtAllHasNoUsage()
    {
        var usage = new UsageTally().Snapshot();

        Assert.Equal(new ReplyUsage(null, null, null, null, null), usage);
    }

    [Fact]
    public void UnavailableCreditsAreNotAZeroChargeAndAddNoCost()
    {
        var tally = new UsageTally();

        tally.Add(inputTokens: 30, outputTokens: 3, reasoningTokens: null, cachedInputTokens: null, totalNanoAiu: 4_000_000_000, creditsUnavailable: true);

        var usage = tally.Snapshot();
        Assert.Equal(30, usage.InputTokens);
        Assert.Equal(3, usage.OutputTokens);
        Assert.Null(usage.AiCredits);
    }

    [Fact]
    public void AnUnavailableEventDoesNotCancelTheCreditsOfAReportedOne()
    {
        var tally = new UsageTally();

        tally.Add(inputTokens: 10, outputTokens: 1, reasoningTokens: null, cachedInputTokens: null, totalNanoAiu: 4_000_000_000, creditsUnavailable: true);
        tally.Add(inputTokens: 10, outputTokens: 1, reasoningTokens: null, cachedInputTokens: null, totalNanoAiu: 1_500_000_000, creditsUnavailable: false);

        Assert.Equal(1.5, tally.Snapshot().AiCredits);
    }

    [Fact]
    public void NoCopilotUsageMeansNoCreditsEvenWhenTokensAreThere()
    {
        var tally = new UsageTally();

        tally.Add(inputTokens: 10, outputTokens: 1, reasoningTokens: null, cachedInputTokens: null, totalNanoAiu: null, creditsUnavailable: false);

        var usage = tally.Snapshot();
        Assert.Equal(10, usage.InputTokens);
        Assert.Null(usage.AiCredits);
    }

    [Fact]
    public void ConvertsNanoAiuToCreditsAtOneBillionPerCredit()
    {
        var tally = new UsageTally();

        tally.Add(inputTokens: null, outputTokens: null, reasoningTokens: null, cachedInputTokens: null, totalNanoAiu: 12_345_000_000, creditsUnavailable: false);

        Assert.Equal(12.345, tally.Snapshot().AiCredits!.Value, precision: 9);
    }
}
