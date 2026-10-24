using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Chess.Api.Persistence;

/// <summary>
/// Brings the schema up to date after start: <c>MigrateAsync</c> for relational providers,
/// <c>EnsureCreated</c> for InMemory. Then runs the interrupted-game sweep. Runs in the background so
/// the app is reachable while it works; <see cref="DatabaseReadiness"/> reports the outcome.
/// </summary>
public sealed class MigrationHostedService(
    IDbContextFactory<ArenaDbContext> factory,
    DatabaseReadiness readiness,
    TimeProvider clock,
    ILogger<MigrationHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var db = await factory.CreateDbContextAsync(stoppingToken);
            if (db.Database.IsRelational())
            {
                await db.Database.MigrateAsync(stoppingToken);
            }
            else
            {
                await db.Database.EnsureCreatedAsync(stoppingToken);
            }

            var swept = await InterruptedGameSweep.SweepAsync(db, clock.GetUtcNow(), stoppingToken);
            readiness.MarkReady(swept);
            logger.LogInformation("Database ready; closed {Count} interrupted game(s)", swept);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Database migration failed");
            readiness.MarkFailed(ex.Message);
        }
    }
}
