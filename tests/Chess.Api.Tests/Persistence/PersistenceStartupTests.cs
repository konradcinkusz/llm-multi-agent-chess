using Chess.Api.Persistence;
using Chess.Api.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Chess.Api.Tests.Persistence;

public class PersistenceStartupTests
{
    private static HostApplicationBuilder NewBuilder(params (string Key, string? Value)[] settings)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(settings.ToDictionary(s => s.Key, s => s.Value));
        return builder;
    }

    [Fact]
    public void Unknown_provider_is_rejected_at_registration()
    {
        var builder = NewBuilder(("Database:Provider", "Oracle"));

        var ex = Assert.Throws<InvalidOperationException>(() => builder.AddArenaPersistence());
        Assert.Contains("Oracle", ex.Message);
    }

    [Fact]
    public void PostgreSql_without_a_connection_string_is_rejected_at_registration()
    {
        var builder = NewBuilder(("Database:Provider", "PostgreSQL"));

        var ex = Assert.Throws<InvalidOperationException>(() => builder.AddArenaPersistence());
        Assert.Contains("ConnectionStrings:chess", ex.Message);
    }

    [Fact]
    public async Task PostgreSql_with_a_connection_string_selects_npgsql_without_connecting()
    {
        var builder = NewBuilder(
            ("Database:Provider", "PostgreSQL"),
            ("ConnectionStrings:chess", "Host=127.0.0.1;Port=1;Database=chess"));
        builder.AddArenaPersistence();

        using var host = builder.Build();
        var factory = host.Services.GetRequiredService<IDbContextFactory<ArenaDbContext>>();
        await using var context = await factory.CreateDbContextAsync();

        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", context.Database.ProviderName);
        Assert.True(context.Database.IsRelational());
    }

    [Fact]
    public async Task InMemory_startup_creates_the_schema_closes_interrupted_games_and_reports_healthy()
    {
        var databaseName = Guid.NewGuid().ToString();
        var interrupted = Guid.NewGuid();
        await using (var seed = new ArenaDbContext(new DbContextOptionsBuilder<ArenaDbContext>()
                         .UseInMemoryDatabase(databaseName).Options))
        {
            seed.Games.Add(new GameEntity
            {
                Id = interrupted, CreatedAt = DateTimeOffset.UnixEpoch, StartedAt = DateTimeOffset.UnixEpoch, Result = "*",
            });
            await seed.SaveChangesAsync();
        }

        var builder = NewBuilder(
            ("Database:Provider", "InMemory"),
            ("Database:InMemoryName", databaseName));
        builder.AddArenaPersistence();

        using var host = builder.Build();
        await host.StartAsync();
        try
        {
            var readiness = host.Services.GetRequiredService<DatabaseReadiness>();
            await readiness.Finished.WaitAsync(TimeSpan.FromSeconds(30));

            Assert.Equal(DatabaseState.Ready, readiness.Current.State);
            Assert.Equal(1, readiness.Current.SweptGames);

            var report = await host.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync();
            Assert.Equal(HealthStatus.Healthy, report.Entries["database"].Status);

            await using var db = host.Services.GetRequiredService<IDbContextFactory<ArenaDbContext>>()
                .CreateDbContext();
            var game = await db.Games.SingleAsync(g => g.Id == interrupted);
            Assert.Equal("interrupted", game.Termination);
            Assert.NotNull(game.FinishedAt);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task Health_is_degraded_while_migrating_healthy_after_and_unhealthy_on_failure()
    {
        var readiness = new DatabaseReadiness();
        var check = new DatabaseHealthCheck(readiness);

        var migrating = await check.CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Degraded, migrating.Status);

        readiness.MarkReady(0);
        var ready = await check.CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Healthy, ready.Status);

        var failed = new DatabaseReadiness();
        failed.MarkFailed("relation \"games\" does not exist");
        var unhealthy = await new DatabaseHealthCheck(failed).CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Unhealthy, unhealthy.Status);
        Assert.Equal("relation \"games\" does not exist", unhealthy.Description);
    }
}
