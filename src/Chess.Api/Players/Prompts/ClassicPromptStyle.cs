namespace Chess.Api.Players.Prompts;

/// <summary>
/// The classic style: <see cref="RatedPromptBuilder"/>'s texts, byte-identical to the rated prompt before prompt styles existed
/// (ADR-015). One conversation per game. Its version is pinned by a test, so a change here shows up in every fingerprint.
/// </summary>
public sealed class ClassicPromptStyle : IPromptStyle
{
    public string Id => "classic";

    public string DisplayName => "Classic";

    public string Description => "The rated prompt: the FEN, the last ten moves and the legal moves. One conversation per game.";

    public bool PerMoveConversation => false;

    public string SystemPrompt => RatedPromptBuilder.SystemPrompt;

    public string Build(MoveRequest request, string? plan) => RatedPromptBuilder.Build(request);

    public string Corrective(string? rejectedMove, IReadOnlyList<string> legalSan) =>
        RatedPromptBuilder.Corrective(rejectedMove, legalSan);

    public string MoveText(string reply) => reply;

    public string? PlanOf(string reply) => null;
}
