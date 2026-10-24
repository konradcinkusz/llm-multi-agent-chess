using Xunit;

namespace Chess.E2E;

/// <summary>
/// The journeys share one AppHost (ArenaFixture) and run one after another: they write to the same database,
/// and the Results page reads everything rated so far.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ArenaCollection : ICollectionFixture<ArenaFixture>
{
    public const string Name = "Arena";
}
