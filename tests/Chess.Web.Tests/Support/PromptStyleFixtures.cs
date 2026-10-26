namespace Chess.Web.Tests.Support;

/// <summary>What <c>GET /api/prompt-styles</c> answers (03 §14.3): classic first, then board and think.</summary>
internal static class PromptStyleFixtures
{
    public const string Json = """
        [
          {"id":"classic","displayName":"Classic","description":"Today's rated prompt.","perMoveConversation":false,"version":"sha256:000000000000"},
          {"id":"board","displayName":"Board","description":"The position as an ASCII board, with piece lists and the full game.","perMoveConversation":true,"version":"sha256:111111111111"},
          {"id":"think","displayName":"Think, then move","description":"An analysis, a plan and one move.","perMoveConversation":true,"version":"sha256:222222222222"}
        ]
        """;
}
