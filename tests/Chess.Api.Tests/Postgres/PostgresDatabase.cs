using Npgsql;

namespace Chess.Api.Tests.Postgres;

/// <summary>
/// A throw-away database on the server named by <c>ARENA_TEST_POSTGRES</c>: created empty on construction and dropped on
/// dispose, so each test sees its own schema and the tests do not interfere.
/// </summary>
public sealed class PostgresDatabase : IDisposable
{
    private readonly NpgsqlConnectionStringBuilder _admin;

    public PostgresDatabase()
    {
        var server = Environment.GetEnvironmentVariable(PostgresFactAttribute.Variable)
            ?? throw new InvalidOperationException($"{PostgresFactAttribute.Variable} is not set.");
        _admin = new NpgsqlConnectionStringBuilder(server) { Pooling = false };
        Name = $"arena_test_{Guid.NewGuid():N}";
        Execute(_admin.ConnectionString, $"CREATE DATABASE \"{Name}\"");
    }

    public string Name { get; }

    /// <summary>The connection string of the new database, for <c>ConnectionStrings:chess</c>.</summary>
    public string ConnectionString =>
        new NpgsqlConnectionStringBuilder(_admin.ConnectionString) { Database = Name, Pooling = true }.ConnectionString;

    /// <summary>Runs a query against the new database and returns its first value, to inspect what is really stored.</summary>
    public T Scalar<T>(string sql)
    {
        var direct = new NpgsqlConnectionStringBuilder(_admin.ConnectionString) { Database = Name, Pooling = false };
        using var connection = new NpgsqlConnection(direct.ConnectionString);
        connection.Open();
        using var command = new NpgsqlCommand(sql, connection);
        return (T)command.ExecuteScalar()!;
    }

    public void Dispose()
    {
        NpgsqlConnection.ClearAllPools();
        Execute(_admin.ConnectionString, $"DROP DATABASE IF EXISTS \"{Name}\" WITH (FORCE)");
    }

    private static void Execute(string connectionString, string sql)
    {
        using var connection = new NpgsqlConnection(connectionString);
        connection.Open();
        using var command = new NpgsqlCommand(sql, connection);
        command.ExecuteNonQuery();
    }
}
