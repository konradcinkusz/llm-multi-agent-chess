using Xunit;

namespace Chess.Api.Tests.Postgres;

/// <summary>
/// A test that runs only when <c>ARENA_TEST_POSTGRES</c> holds a connection string to a PostgreSQL server (any database on it,
/// with a role that may create databases); otherwise it is skipped. Every test makes its own database there and drops it.
/// </summary>
public sealed class PostgresFactAttribute : FactAttribute
{
    public const string Variable = "ARENA_TEST_POSTGRES";

    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Variable)))
        {
            Skip = $"Set {Variable} to a PostgreSQL connection string to run this test (see README, Tests and CI).";
        }
    }
}

/// <summary>A theory that runs only when <c>ARENA_TEST_POSTGRES</c> is set, like <see cref="PostgresFactAttribute"/>.</summary>
public sealed class PostgresTheoryAttribute : TheoryAttribute
{
    public PostgresTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PostgresFactAttribute.Variable)))
        {
            Skip = $"Set {PostgresFactAttribute.Variable} to a PostgreSQL connection string to run this test (see README, Tests and CI).";
        }
    }
}
