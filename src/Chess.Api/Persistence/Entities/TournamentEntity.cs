using Chess.Api.Tournaments;

namespace Chess.Api.Persistence.Entities;

/// <summary>One row of <c>tournaments</c>. <see cref="Models"/> is stored as a jsonb array of strings.</summary>
public sealed class TournamentEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public List<string> Models { get; set; } = [];
    public int GamesPerPair { get; set; }
    public int MaxConcurrency { get; set; }
    public string Status { get; set; } = "running";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>What the run is: a round robin, or a benchmark (<see cref="TournamentKind"/>).</summary>
    public string Kind { get; set; } = TournamentKind.RoundRobin;

    /// <summary>The candidate of a benchmark (03 §12.2); null for a round robin.</summary>
    public string? Candidate { get; set; }

    /// <summary>
    /// The build that played a benchmark (docs/BENCHMARK.md §5), stored when the run was created: the app version, the rating
    /// formula version and the prompt version. Null for a round robin and for a benchmark created before these were stored;
    /// the report then uses the current values.
    /// </summary>
    public string? AppVersion { get; set; }

    public string? FormulaVersion { get; set; }

    public string? PromptVersion { get; set; }
}
