using Chess.Api.Players;
using Chess.Api.Tests.Players;
using Chess.Llm;
using Xunit;

namespace Chess.Api.Tests.Evals;

/// <summary>
/// The offline eval: every scenario runs through <see cref="LlmChessPlayer"/> with the scripted <see cref="FakeLlmGateway"/>,
/// so no network and no subscription are needed. The assertions are on the trace of the move (AI-EVALS §4): the replies
/// the side made, in order, which of them were legal, and whether the side forfeited or played the expected SAN.
/// </summary>
public class OfflineEvalTests
{
    private const string Model = "eval-model";

    private static readonly int MaxAttempts = new PlayerOptions().MaxAttemptsPerMove;

    public static TheoryData<EvalScenario> Scenarios()
    {
        var data = new TheoryData<EvalScenario>();
        foreach (var scenario in EvalScenarios.All)
        {
            data.Add(scenario);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public async Task ScenarioResolvesOrForfeitsAsItsLabelSays(EvalScenario scenario)
    {
        var gateway = new FakeLlmGateway(seed: 1);
        gateway.Script(Model, scenario.Replies.ToArray());
        var conversation = await gateway.StartConversationAsync(
            new LlmConversationOptions(Model, RatedPromptBuilder.SystemPrompt, TimeSpan.FromSeconds(5)), CancellationToken.None);
        await using var player = new LlmChessPlayer(new PlayerDescriptor("llm", Model), conversation, MaxAttempts);

        var move = await player.ChooseMoveAsync(PlayerTestData.RequestFor(scenario.Fen, []), CancellationToken.None);

        // Exactly the scripted replies were attempted: no reply left over, none invented by the fake.
        Assert.Equal(scenario.Replies.ToArray(), move.Attempts.Select(a => a.RawReply).ToArray());

        // Only the last reply is legal when the side plays; no reply is legal when it forfeits.
        var expectedLegal = new bool[scenario.Replies.Count];
        if (scenario.ExpectedSan is not null)
        {
            expectedLegal[^1] = true;
        }

        Assert.Equal(expectedLegal, move.Attempts.Select(a => a.Legal).ToArray());

        Assert.Equal(scenario.ExpectedSan is null, move.Forfeit);
        Assert.Equal(scenario.ExpectedSan, move.San);
    }
}
