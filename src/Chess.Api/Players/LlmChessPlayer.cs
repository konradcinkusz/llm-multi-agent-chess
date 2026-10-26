using System.Diagnostics;
using Chess.Api.Players.Prompts;
using Chess.Engine;
using Chess.Llm;

namespace Chess.Api.Players;

/// <summary>
/// An LLM side of one game. A classic side keeps one conversation for the whole game. A per-move style (board, think) opens one
/// conversation for each move from <c>openMoveConversation</c> and disposes of it after the move, retries included (03 §14.2).
/// A reply counts only when <see cref="MoveNotation.TryResolve"/> maps its move to a legal move. A failed reply gets a corrective
/// follow-up in the same conversation, up to <paramref name="maxAttempts"/>; after that the side forfeits. There is no fallback move.
/// </summary>
public sealed class LlmChessPlayer : IChessPlayer
{
    private readonly ILlmConversation? _gameConversation;
    private readonly Func<CancellationToken, Task<ILlmConversation>>? _openMoveConversation;
    private readonly IPromptStyle _style;
    private readonly int _maxAttempts;
    private string? _plan;

    /// <summary>A side with one conversation for the whole game: the classic style, unless another style is given.</summary>
    public LlmChessPlayer(PlayerDescriptor descriptor, ILlmConversation conversation, int maxAttempts, IPromptStyle? style = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);
        ArgumentNullException.ThrowIfNull(conversation);

        _style = style ?? PromptStyles.Classic;
        if (_style.PerMoveConversation)
        {
            throw new ArgumentException($"The {_style.Id} style opens a conversation per move: use the constructor that takes its factory.", nameof(style));
        }

        Descriptor = descriptor;
        _gameConversation = conversation;
        _maxAttempts = maxAttempts;
    }

    /// <summary>A side with one conversation per move, opened by <paramref name="openMoveConversation"/> (board, think).</summary>
    public LlmChessPlayer(PlayerDescriptor descriptor, IPromptStyle style, Func<CancellationToken, Task<ILlmConversation>> openMoveConversation,
        int maxAttempts)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);
        ArgumentNullException.ThrowIfNull(openMoveConversation);

        if (!style.PerMoveConversation)
        {
            throw new ArgumentException($"The {style.Id} style keeps one conversation per game: use the constructor that takes its conversation.", nameof(style));
        }

        Descriptor = descriptor;
        _style = style;
        _openMoveConversation = openMoveConversation;
        _maxAttempts = maxAttempts;
    }

    public PlayerDescriptor Descriptor { get; }

    public async Task<PlayerMove> ChooseMoveAsync(MoveRequest request, CancellationToken ct)
    {
        if (_gameConversation is not null)
        {
            return await PlayMoveAsync(_gameConversation, request, ct);
        }

        await using var moveConversation = await _openMoveConversation!(ct);
        return await PlayMoveAsync(moveConversation, request, ct);
    }

    public ValueTask DisposeAsync() => _gameConversation?.DisposeAsync() ?? ValueTask.CompletedTask;

    private async Task<PlayerMove> PlayMoveAsync(ILlmConversation conversation, MoveRequest request, CancellationToken ct)
    {
        var board = new ChessBoard();
        board.LoadFen(request.Fen);

        var attempts = new List<MoveAttempt>(_maxAttempts);
        var prompt = _style.Build(request, _plan);
        for (var number = 1; number <= _maxAttempts; number++)
        {
            var attempt = await AskOnceAsync(conversation, prompt, number, board, ct);
            attempts.Add(attempt);
            if (attempt.Legal)
            {
                _plan = _style.PlanOf(attempt.RawReply!);
                return new PlayerMove(attempt.ParsedSan, attempts, Forfeit: false);
            }

            prompt = _style.Corrective(attempt.RawReply is { } raw ? _style.MoveText(raw) : null, request.LegalSan);
        }

        return new PlayerMove(null, attempts, Forfeit: true);
    }

    private async Task<MoveAttempt> AskOnceAsync(ILlmConversation conversation, string prompt, int number, ChessBoard board, CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        try
        {
            var reply = await conversation.AskAsync(prompt, ct);
            var move = _style.MoveText(reply.Text);
            var legal = MoveNotation.TryResolve(board, move, out var san);
            var rejection = legal ? null : RejectionClassifier.Classify(board, move);
            return new MoveAttempt(number, prompt, reply.Text, legal ? san : null, legal, clock.Elapsed,
                reply.InputTokens, reply.OutputTokens, rejection?.Reason,
                reply.Reasoning, reply.ReasoningTokens, reply.CachedInputTokens, reply.AiCredits, rejection?.Kind);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A timeout or a gateway failure is an attempt like any other, so the game goes on or forfeits.
            var kind = ex is TimeoutException ? RejectionClassifier.Timeout : RejectionClassifier.Error;
            var reason = kind == RejectionClassifier.Timeout
                ? $"{kind}: {ex.Message}"
                : $"{kind}: {ex.GetType().Name}: {ex.Message}";
            return new MoveAttempt(number, prompt, null, null, false, clock.Elapsed, null, null, reason, RejectionKind: kind);
        }
    }
}
