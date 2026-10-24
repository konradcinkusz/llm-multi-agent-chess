namespace Chess.Api.Tests.Evals;

/// <summary>
/// The outcome a scenario expects. <c>Accepted</c>: the first reply is the move. <c>Recovers</c>: one or more
/// replies are rejected, then a legal one is taken. <c>Forfeits</c>: every allowed attempt is rejected (AI-EVALS §3).
/// </summary>
public enum ScenarioClass { Accepted, Recovers, Forfeits }

/// <summary>
/// One position and the replies a model gives for it, in order. <see cref="Replies"/> are exactly the attempts the
/// side makes: when <see cref="ExpectedSan"/> is set, only the last reply is legal; when it is null, the side forfeits
/// and every reply is rejected.
/// </summary>
public sealed record EvalScenario(
    string Id,
    ScenarioClass Class,
    string Fen,
    IReadOnlyList<string> Replies,
    string? ExpectedSan)
{
    public override string ToString() => Id;
}
