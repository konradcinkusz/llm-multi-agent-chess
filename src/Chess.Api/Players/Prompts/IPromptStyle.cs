namespace Chess.Api.Players.Prompts;

/// <summary>
/// One prompt style (03 §14.2): the texts a player sends and the way its replies are read. Pure: the same request always
/// gives the same text. Classic is today's rated prompt. Board and think add the goal, the board as text and the whole game,
/// and they open one conversation per move.
/// </summary>
public interface IPromptStyle
{
    /// <summary>The name in a player id, as in <c>gpt~think</c>.</summary>
    string Id { get; }

    string DisplayName { get; }

    string Description { get; }

    /// <summary>True when each move opens its own conversation (board, think); false when the game keeps one (classic).</summary>
    bool PerMoveConversation { get; }

    /// <summary>The system prompt of every conversation of this style.</summary>
    string SystemPrompt { get; }

    /// <summary>The prompt for one move. <paramref name="plan"/> is the plan carried from the player's last move, or null.</summary>
    string Build(MoveRequest request, string? plan);

    /// <summary>The follow-up after an illegal move, or after no reply at all (<paramref name="rejectedMove"/> null).</summary>
    string Corrective(string? rejectedMove, IReadOnlyList<string> legalSan);

    /// <summary>The text the move is read from: the whole reply, or for think the text after its last <c>Move:</c> line.</summary>
    string MoveText(string reply);

    /// <summary>The plan a reply carries, or null when it has none.</summary>
    string? PlanOf(string reply);
}
