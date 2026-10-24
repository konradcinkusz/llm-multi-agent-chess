using Chess.Api.Players;
using Chess.Engine;
using Chess.Llm;
using Xunit;

namespace Chess.Api.Tests.Players;

/// <summary>A conversation whose replies are scripted per call; it records every prompt it was sent.</summary>
internal sealed class ScriptedConversation(params Func<LlmReply>[] replies) : ILlmConversation
{
    private readonly Queue<Func<LlmReply>> _replies = new(replies);

    public List<string> Prompts { get; } = [];

    public string ModelId => "scripted";

    public Task<LlmReply> AskAsync(string prompt, CancellationToken ct)
    {
        Prompts.Add(prompt);
        return Task.FromResult(_replies.Dequeue()());
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public static Func<LlmReply> Reply(string text) => () => new LlmReply(text, TimeSpan.Zero, 10, 2);
}

/// <summary>A gateway in a fixed state that never starts a conversation, for the factory's refusal paths.</summary>
internal sealed class StubGateway(LlmGatewayState state) : ILlmGateway
{
    public LlmGatewayStatus Status { get; } = new(state, "Run `copilot` and log in.");

    public Task<IReadOnlyList<LlmModelInfo>> ListModelsAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<LlmModelInfo>>([]);

    public Task<ILlmConversation> StartConversationAsync(LlmConversationOptions options, CancellationToken ct) =>
        throw new InvalidOperationException("The factory must check the gateway state before starting a conversation.");
}

internal static class PlayerTestData
{
    public const string StartFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    /// <summary>A request for the start position with the engine's own legal moves, so the tests never hand-write them.</summary>
    public static MoveRequest StartRequest() => RequestFor(StartFen, []);

    public static MoveRequest RequestFor(string fen, IReadOnlyList<string> history)
    {
        var board = new ChessBoard();
        board.LoadFen(fen);
        var legal = board.GetLegalMoves().Select(m => m.San).ToList();
        return new MoveRequest(Guid.NewGuid(), history.Count + 1, fen, legal, history);
    }

    /// <summary>The FEN after the given SAN moves from the start position, produced by the engine.</summary>
    public static string FenAfter(params string[] sanMoves)
    {
        var board = new ChessBoard();
        board.LoadFen(StartFen);
        foreach (var san in sanMoves)
        {
            Assert.True(board.ApplyMoveSan(san), $"'{san}' is not legal in the test line.");
        }

        return board.ToFen();
    }
}
