using System.Text.Json;
using Chess.Web.Api;

namespace Chess.Web.Components.Pages.Play;

/// <summary>
/// The game on the live page: the server's state as loaded (GET) and the events applied on top of it (SSE).
/// Plies are 1-based; ply 1 is White's first move. The ply rule of 03 §5.1 lives here.
/// </summary>
public sealed class LiveGame
{
    public const string StartFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly List<MoveDto> _moves = [];
    private readonly List<AttemptNotice> _notices = [];

    public LiveGame(GameSummaryDto summary)
    {
        Summary = summary;
    }

    public GameSummaryDto Summary { get; private set; }

    public Guid GameId => Summary.Id;

    public string CurrentFen { get; private set; } = StartFen;

    /// <summary>The highest ply applied. An event for this ply or a lower one is already known and is ignored.</summary>
    public int AppliedPly { get; private set; }

    public IReadOnlyList<MoveDto> Moves => _moves;

    public IReadOnlyList<AttemptNotice> Notices => _notices;

    /// <summary>The side the server is asking a model for right now, or null between moves.</summary>
    public ThinkingState? Thinking { get; private set; }

    /// <summary>Null while the game is still going.</summary>
    public GameOutcome? Outcome { get; private set; }

    public bool IsFinished => Outcome is not null;

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
    /// Applies one event. A <see cref="ApplyResult.Gap"/> means a ply was missed before this one, so the
    /// caller should load the game again instead of guessing the missing move.
    /// </summary>
    public ApplyResult Apply(GameEventMessage message)
    {
        switch (message.Type)
        {
            case "MoveStarted":
            {
                var e = Read<MoveStartedEvent>(message);
                if (e.Ply <= AppliedPly)
                {
                    return ApplyResult.Duplicate;
                }

                Thinking = new ThinkingState(e.Ply, e.Color, e.ModelId);
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

                _moves.Add(new MoveDto(e.Ply, e.Color, e.San, e.Fen, e.Attempts, e.LatencyMs, null, null, null, null));
                CurrentFen = e.Fen;
                AppliedPly = e.Ply;
                Thinking = null;
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

public sealed record ThinkingState(int Ply, string Color, string ModelId);

/// <summary>PGN-style result and the termination reason (03 §3.3).</summary>
public sealed record GameOutcome(string Result, string? Termination);

/// <summary>An illegal reply, shown inline. The move is retried or the side forfeits; there is no fallback move.</summary>
public sealed record AttemptNotice(int Ply, string Color, int Attempt, string Reason, string? RawReply);
