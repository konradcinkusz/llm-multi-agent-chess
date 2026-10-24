using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Chess.Api.Persistence;

public static class PersistenceExtensions
{
    /// <summary>
    /// Registers the context factory for <c>Database:Provider</c> (<c>PostgreSQL</c> by default, or
    /// <c>InMemory</c> for tests and the fake profile), the migration hosted service and the
    /// <c>database</c> health check. PostgreSQL needs <c>ConnectionStrings:chess</c>, supplied by Aspire.
    /// </summary>
    public static IHostApplicationBuilder AddArenaPersistence(this IHostApplicationBuilder builder)
    {
        var database = builder.Configuration.GetSection("Database");
        var provider = database["Provider"] ?? "PostgreSQL";

        switch (provider)
        {
            case "PostgreSQL":
                var connectionString = builder.Configuration.GetConnectionString("chess")
                    ?? throw new InvalidOperationException(
                        "Database:Provider is PostgreSQL but ConnectionStrings:chess is not set. Run through Chess.AppHost, or set the connection string in user secrets.");
                builder.Services.AddDbContextFactory<ArenaDbContext>(options => options.UseNpgsql(
                    connectionString,
                    npgsql => npgsql.EnableRetryOnFailure(10, TimeSpan.FromSeconds(30), null).CommandTimeout(60)));
                break;

            case "InMemory":
                var databaseName = database["InMemoryName"] ?? "chess-arena";
                builder.Services.AddDbContextFactory<ArenaDbContext>(options =>
                    options.UseInMemoryDatabase(databaseName));
                break;

            default:
                throw new InvalidOperationException(
                    $"Unknown Database:Provider '{provider}'. Use 'PostgreSQL' or 'InMemory'.");
        }

        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<DatabaseReadiness>();
        builder.Services.AddHostedService<MigrationHostedService>();
        builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");
        return builder;
    }
}
