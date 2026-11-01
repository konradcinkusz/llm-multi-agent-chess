namespace Chess.Api.Results.Archive;

/// <summary>Bound from <c>Arena:Results:Archive</c>. Both folders are empty by default: the archive is opt-in.</summary>
public sealed class ArchiveOptions
{
    /// <summary>A folder read at start-up; its games are added to the database. Set by the CI job that builds the public site.</summary>
    public string? ImportFolder { get; set; }

    /// <summary>The folder <c>POST /api/results/archive/export</c> writes into: <c>results/</c> of a clone of the repository.</summary>
    public string? ExportFolder { get; set; }
}
