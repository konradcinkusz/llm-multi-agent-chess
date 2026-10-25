using System.Text.Json;
using Chess.Web.Api;
using Chess.Web.Components.Shared;
using Chess.Web.Components.Shared.Game;

namespace Chess.Web.Components.Pages.Play;

/// <summary>
/// The game on the live page: the server's state as loaded (GET) and the events applied on top of it (SSE), plus every
/// attempt the engine log shows. Plies are 1-based; ply 1 is White's first move. The ply rule of 03 §5.1 lives here.
/// </summary>
public sealed class LiveGame
{
    public const string StartFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly List<MoveDto> _moves = [];
    private readonly List<AttemptNotice> _notices = [];
    private readonly Dictionary<(int Ply, int Attempt), EngineEntry> _attempts = [];

    public LiveGame(GameSummaryDto summary)
    {
        Summary = summary;
    }

    public GameSummaryDto Summary { get; private set; }

    public Guid GameId => Summary.Id;

    public string CurrentFen { get; private set; } = StartFen;

    /// <summary>The highest ply applied. An event for this ply or a lower one is already known and is ignored.</summary>
    public int AppliedPly { get; private set; }

    /// <summary>Counts every change to the server's state, so a view built from it can tell it is out of date.</summary>
    public int Version { get; private set; }

    /// <summary>The ply the game is waiting for next.</summary>
    public int NextPly => AppliedPly + 1;

    public IReadOnlyList<MoveDto> Moves => _moves;

    public IReadOnlyList<AttemptNotice> Notices => _notices;

    /// <summary>Every attempt seen so far, in ply and attempt order.</summary>
    public IReadOnlyList<EngineEntry> Attempts => _attempts.Values
        .OrderBy(entry => entry.Ply)
        .ThenBy(entry => entry.Attempt)
        .ToArray();

    /// <summary>The side the server is asking a model for right now, or null between moves.</summary>
    public ThinkingState? Thinking { get; private set; }

    /// <summary>Null while the game is still going.</summary>
    public GameOutcome? Outcome { get; private set; }

    public bool IsFinished => Outcome is not null;

    /// <summary>The squares of the last move, or null before the first move.</summary>
    public BoardMove? LastMove
    {
        get
        {
            if (_moves.Count == 0)
            {
                return null;
            }

            var previous = _moves.Count == 1 ? StartFen : _moves[^2].FenAfter;
            return BoardFacts.LastMove(previous, _moves[^1].FenAfter, _moves[^1].San);
        }
    }

    /// <summary>The king in check after the last move, or null.</summary>
    public string? CheckSquare => _moves.Count == 0 ? null : BoardFacts.CheckedKing(CurrentFen, _moves[^1].San);

    /// <summary>
    /// Applies the game as the server reports it. A report older than the events already applied is
    /// ignored, because those events are newer than it.
    /// </summary>
    public void Load(GameDetailDto detail)
    {
        if (detail.Summary.PlyCount < AppliedPly)
        {
            return;
        }

        Version++;
        Summary = detail.Summary;
        CurrentFen = detail.CurrentFen;
        AppliedPly = detail.Summary.PlyCount;
        _moves.Clear();
        _moves.AddRange(detail.Moves);

        if (detail.Summary.State == "finished")
        {
            Outcome = new GameOutcome(detail.Summary.Result, detail.Summary.Termination);
            Thinking = null;
        }
    }

    /// <summary>
    /// Adds attempts the game already had (GET /api/attempts). An attempt that arrived as an event is kept as it is:
    /// the same attempt twice is one entry.
    /// </summary>
    public void AddAttempts(IEnumerable<EngineEntry> attempts)
    {
        Version++;
        foreach (var attempt in attempts)
        {
            _attempts.TryAdd((attempt.Ply, attempt.Attempt), attempt);
        }
    }

    /// <summary>
    /// Applies one event. A <see cref="ApplyResult.Gap"/> means a ply was missed before this one, so the
    /// caller should load the game again instead of guessing the missing move.
    /// </summary>
    public ApplyResult Apply(GameEventMessage message)
    {
        Version++;
        switch (message.Type)
        {
            case "MoveStarted":
            {
                var e = Read<MoveStartedEvent>(message);
                if (e.Ply <= AppliedPly)
                {
                    return ApplyResult.Duplicate;
                }

                // The side comes from the ply: the API sends "white" and "black" in lower case, the page says "White".
                Thinking = new ThinkingState(e.Ply, ColorOf(e.Ply), e.ModelId, e.At, 1);
                MarkRunning();
                return ApplyResult.Applied;
            }
            case "AttemptRecorded":
            {
                var e = Read<AttemptRecordedEvent>(message);
                _attempts[(e.Ply, e.Attempt)] = new EngineEntry(e.Ply, e.Attempt, e.ModelId, e.Prompt, e.RawReply, e.Reasoning,
                    e.ParsedSan, e.Legal, e.RejectionKind, e.LatencyMs, e.InputTokens, e.OutputTokens, e.ReasoningTokens,
                    e.AiCredits, e.At);
                MarkRunning();

                // A legal attempt ends the move (MovePlayed follows); a rejected one sends the model to its next attempt.
                if (!e.Legal)
                {
                    NextAttempt(e.Ply, e.Attempt);
                }

                return ApplyResult.Applied;
            }
            case "AttemptRejected":
            {
                var e = Read<AttemptRejectedEvent>(message);
                if (e.Ply <= AppliedPly)
                {
                    return ApplyResult.Duplicate;
                }

                _notices.Add(new AttemptNotice(e.Ply, ColorOf(e.Ply), e.Attempt, e.Reason, e.RawReply));
                NextAttempt(e.Ply, e.Attempt);
                return ApplyResult.Applied;
            }
            case "MovePlayed":
            {
                var e = Read<MovePlayedEvent>(message);
                if (e.Ply <= AppliedPly)
                {
                    return ApplyResult.Duplicate;
                }

                if (e.Ply > AppliedPly + 1)
                {
                    return ApplyResult.Gap;
                }

                _moves.Add(new MoveDto(e.Ply, ColorOf(e.Ply), e.San, e.Fen, e.Attempts, e.LatencyMs, null, null, null, null));
                CurrentFen = e.Fen;
                AppliedPly = e.Ply;
                Thinking = null;
                MarkRunning();
                return ApplyResult.Applied;
            }
            case "GameFinished":
            {
                var e = Read<GameFinishedEvent>(message);
                Outcome = new GameOutcome(e.Result, e.Termination);
                Thinking = null;
                return ApplyResult.Applied;
            }
            default:
                return ApplyResult.Unknown;
        }
    }

    /// <summary>
    /// The game as the viewer has been shown it up to <paramref name="ply"/> (03 §10.5): the moves up to that ply, and the
    /// engine log up to the next one, so an attempt comes with or before its move. The thinking and the outcome appear only
    /// once the viewer has reached the latest ply the game has.
    /// </summary>
    public LiveGame ShownUpTo(int ply)
    {
        var upTo = Math.Clamp(ply, 0, AppliedPly);
        var shown = new LiveGame(Summary);
        shown._moves.AddRange(_moves.Where(move => move.Ply <= upTo));
        shown._notices.AddRange(_notices.Where(notice => notice.Ply <= upTo + 1));
        foreach (var (key, entry) in _attempts.Where(pair => pair.Key.Ply <= upTo + 1))
        {
            shown._attempts[key] = entry;
        }

        shown.AppliedPly = upTo;
        shown.CurrentFen = shown._moves.Count == 0 ? StartFen : shown._moves[^1].FenAfter;
        if (upTo == AppliedPly)
        {
            shown.Thinking = Thinking;
            shown.Outcome = Outcome;
        }

        return shown;
    }

    /// <summary>The model thinking about this ply is on the attempt after <paramref name="attempt"/>. Idempotent: the rejection and the record of one attempt both say so.</summary>
    private void NextAttempt(int ply, int attempt)
    {
        if (Thinking is { } thinking && thinking.Ply == ply && attempt >= thinking.Attempt)
        {
            Thinking = thinking with { Attempt = attempt + 1 };
        }
    }

    /// <summary>
    /// The summary is loaded once, before the game starts; a game that is playing (an event arrived) is running, whatever
    /// the summary said when it was loaded.
    /// </summary>
    private void MarkRunning()
    {
        if (Summary.State == "queued")
        {
            Summary = Summary with { State = "running" };
        }
    }

    /// <summary>Ply 1 is White's, then they alternate.</summary>
    public static string ColorOf(int ply) => ply % 2 == 1 ? "White" : "Black";

    private static T Read<T>(GameEventMessage message) =>
        message.Data.Deserialize<T>(JsonOptions)
        ?? throw new InvalidOperationException($"Empty {message.Type} event.");
}

public enum ApplyResult
{
    Applied,

    /// <summary>The event is for a ply the game already has.</summary>
    Duplicate,

    /// <summary>A move before this one was never received; the game must be loaded again.</summary>
    Gap,

    /// <summary>An event type the page does not show.</summary>
    Unknown,
}

/// <summary>
/// The move being decided: its ply and side, the model, when it started (the server's clock) and the attempt the model is on
/// (1 to 3, the attempt after each illegal reply).
/// </summary>
public sealed record ThinkingState(int Ply, string Color, string ModelId, DateTimeOffset StartedAt, int Attempt);

/// <summary>PGN-style result and the termination reason (03 §3.3).</summary>
public sealed record GameOutcome(string Result, string? Termination);

/// <summary>An illegal reply, shown inline. The move is retried or the side forfeits; there is no fallback move.</summary>
public sealed record AttemptNotice(int Ply, string Color, int Attempt, string Reason, string? RawReply);
