namespace Chess.Llm;

/// <summary>
/// Picks the readable reasoning of one reply. The final assistant message's reasoning wins; when it has none, the
/// reasoning blocks streamed as events are joined, in order. Null when the model exposed no reasoning.
/// </summary>
internal static class ReplyReasoning
{
    private const string BlockSeparator = "\n\n";

    public static string? Choose(string? messageReasoning, IReadOnlyList<string> reasoningBlocks)
    {
        if (!string.IsNullOrWhiteSpace(messageReasoning))
        {
            return messageReasoning;
        }

        var blocks = reasoningBlocks.Where(b => !string.IsNullOrWhiteSpace(b)).ToList();
        return blocks.Count == 0 ? null : string.Join(BlockSeparator, blocks);
    }
}
