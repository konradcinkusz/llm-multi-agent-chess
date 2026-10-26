namespace Chess.Api.Players.Prompts;

/// <summary>
/// The think style (03 §14.2): the board style's position, the plan carried from the last move, and an answer in two parts.
/// The model analyses, then ends with <c>Plan:</c> and <c>Move:</c> lines. One conversation per move.
/// </summary>
public sealed class ThinkPromptStyle : IPromptStyle
{
    /// <summary>The longest plan carried into the next prompt, in characters.</summary>
    public const int PlanLimit = 300;

    private const string Answer =
        "Answer with a short analysis, then the line `Plan: <one sentence>` and the line `Move: <SAN>`.";

    public string Id => "think";

    public string DisplayName => "Think, then move";

    public string Description =>
        "As board, plus a short analysis, a plan carried to the next move, and one move. One conversation per move.";

    public bool PerMoveConversation => true;

    public string SystemPrompt =>
        BoardText.Goal + " Think briefly about the threats and your plan. End your reply with two lines: " +
        "`Plan: <one sentence>` and `Move: <one move in standard algebraic notation (SAN)>`.";

    public string Build(MoveRequest request, string? plan) =>
        string.Join('\n', BoardText.Describe(request, plan).Append(Answer));

    public string Corrective(string? rejectedMove, IReadOnlyList<string> legalSan) =>
        string.Join('\n',
            rejectedMove is null ? "No move was received." : $"'{rejectedMove}' is not a legal move.",
            $"Legal moves: {string.Join(", ", legalSan)}",
            Answer);

    /// <summary>The text after the last <c>Move:</c> line (case-insensitive). With no such line the whole reply is read, so a bare SAN still counts.</summary>
    public string MoveText(string reply) => LastValue(reply, "Move:") ?? reply;

    /// <summary>The text after the last <c>Plan:</c> line, capped at <see cref="PlanLimit"/> characters; null when there is none.</summary>
    public string? PlanOf(string reply)
    {
        var plan = LastValue(reply, "Plan:");
        return string.IsNullOrEmpty(plan) ? null : plan.Length <= PlanLimit ? plan : plan[..PlanLimit];
    }

    private static string? LastValue(string reply, string label)
    {
        string? value = null;
        foreach (var line in reply.Split('\n'))
        {
            var text = line.Trim();
            if (text.StartsWith(label, StringComparison.OrdinalIgnoreCase))
            {
                value = text[label.Length..].Trim();
            }
        }

        return value;
    }
}
