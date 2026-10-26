namespace Chess.Api.Players.Prompts;

/// <summary>
/// The board style (03 §14.2): the goal in the system prompt and the position as text (the board, each side's pieces, the
/// material, the whole game and the legal moves in alphabetical order). It answers with one SAN move. One conversation per move.
/// </summary>
public sealed class BoardPromptStyle : IPromptStyle
{
    private const string Answer = "Answer with one move in SAN only.";

    public string Id => "board";

    public string DisplayName => "Board";

    public string Description =>
        "The goal, the board as text, each side's pieces, the material and the whole game. One conversation per move.";

    public bool PerMoveConversation => true;

    public string SystemPrompt => BoardText.Goal + " Each reply is a single move in standard algebraic notation (SAN) and nothing else.";

    public string Build(MoveRequest request, string? plan) =>
        string.Join('\n', BoardText.Describe(request, plan: null).Append(Answer));

    public string Corrective(string? rejectedMove, IReadOnlyList<string> legalSan) =>
        RatedPromptBuilder.Corrective(rejectedMove, legalSan);

    public string MoveText(string reply) => reply;

    public string? PlanOf(string reply) => null;
}
