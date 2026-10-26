using System.Security.Cryptography;
using System.Text;
using Chess.Api.Contracts;
using Chess.Engine;

namespace Chess.Api.Players.Prompts;

/// <summary>
/// The prompt styles a player id may name (03 §14.1). The order is the order of <c>GET /api/prompt-styles</c>. Classic is the
/// default, so a plain id names it. Pure: no I/O.
/// </summary>
public static class PromptStyles
{
    public const string DefaultId = "classic";

    public static IReadOnlyList<IPromptStyle> All { get; } = [new ClassicPromptStyle(), new BoardPromptStyle(), new ThinkPromptStyle()];

    public static IPromptStyle Classic => All[0];

    /// <returns>The style with this id, or null when there is none.</returns>
    public static IPromptStyle? Find(string id) => All.FirstOrDefault(style => style.Id == id);

    /// <summary>
    /// A style's version (03 §14.2): the <c>sha256:</c> prefix of its system prompt, one move prompt and one corrective for a
    /// sample position. The classic version is the one the benchmark fingerprints carried before styles existed.
    /// </summary>
    public static string VersionOf(IPromptStyle style)
    {
        var sample = new MoveRequest(Guid.Empty, 0, ChessBoard.StartingPosition().ToFen(), ["e4", "d4"], []);
        var text = string.Join('\n', style.SystemPrompt, style.Build(sample, plan: null),
            style.Corrective("e5", ["e4", "d4"]));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return "sha256:" + Convert.ToHexString(hash)[..12].ToLowerInvariant();
    }

    public static PromptStyleDto ToDto(this IPromptStyle style) =>
        new(style.Id, style.DisplayName, style.Description, style.PerMoveConversation, VersionOf(style));
}
