using System.Diagnostics;
using Chess.Engine;
using Chess.Llm;

namespace Chess.Api.Players;

/// <summary>
/// An LLM side of one game. It keeps one conversation for the whole game. A reply counts only when
/// <see cref="MoveNotation.TryResolve"/> maps it to a legal move. A failed reply gets a corrective follow-up in the
/// same conversation, up to <paramref name="maxAttempts"/>; after that the side forfeits. There is no fallback move.
/// </summary>
public sealed class LlmChessPlayer : IChessPlayer
{
    private readonly ILlmConversation _conversation;
    private readonly int _maxAttempts;

    public LlmChessPlayer(PlayerDescriptor descriptor, ILlmConversation conversation, int maxAttempts)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);

        Descriptor = descriptor;
        _conversation = conversation;
        _maxAttempts = maxAttempts;
    }

    public PlayerDescriptor Descriptor { get; }

    public async Task<PlayerMove> ChooseMoveAsync(MoveRequest request, CancellationToken ct)
    {
        var board = new ChessBoard();
        board.LoadFen(request.Fen);

        var attempts = new List<MoveAttempt>(_maxAttempts);
        var prompt = RatedPromptBuilder.Build(request);
        for (var number = 1; number <= _maxAttempts; number++)
        {
            var attempt = await AskOnceAsync(prompt, number, board, ct);
            attempts.Add(attempt);
            if (attempt.Legal)
            {
                return new PlayerMove(attempt.ParsedSan, attempts, Forfeit: false);
            }

            prompt = RatedPromptBuilder.Corrective(attempt.RawReply, request.LegalSan);
        }

        return new PlayerMove(null, attempts, Forfeit: true);
    }

    public ValueTask DisposeAsync() => _conversation.DisposeAsync();

    private async Task<MoveAttempt> AskOnceAsync(string prompt, int number, ChessBoard board, CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        try
        {
            var reply = await _conversation.AskAsync(prompt, ct);
            var legal = MoveNotation.TryResolve(board, reply.Text, out var san);
            return new MoveAttempt(number, prompt, reply.Text, legal ? san : null, legal, clock.Elapsed,
                reply.InputTokens, reply.OutputTokens, legal ? null : "Not a legal move in this position.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A timeout or a gateway failure is an attempt like any other, so the game goes on or forfeits.
            return new MoveAttempt(number, prompt, null, null, false, clock.Elapsed, null, null,
                $"{ex.GetType().Name}: {ex.Message}");
        }
    }
}
