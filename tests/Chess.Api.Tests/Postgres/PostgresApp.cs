using Chess.Api.Broadcast;
using Chess.Api.Persistence;
using Chess.Api.Requests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Chess.Api.Tests.Postgres;

/// <summary>
/// The real app on its own PostgreSQL database (<see cref="PostgresDatabase"/>) with the fake gateway: the host applies every
/// migration at start, as in production. Unlike the InMemory apps of the other tests, a query that Npgsql cannot translate or a
/// model that drifted from its migrations fails here.
/// </summary>
public sealed class PostgresApp : WebApplicationFactory<Program>
{
    private readonly bool _quietRequests;

    /// <param name="quietRequests">
    /// Removes the two hosted services that touch the request queue on their own (the broadcast runner plays a queued request when
    /// no schedule runs, and the start-up recovery requeues a playing one), so a test can drive the queue itself.
    /// </param>
    public PostgresApp(bool quietRequests = false)
    {
        _quietRequests = quietRequests;
        Database = new PostgresDatabase();
    }

    public PostgresDatabase Database { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Database:Provider", "PostgreSQL");
        builder.UseSetting("ConnectionStrings:chess", Database.ConnectionString);
        builder.UseSetting("Llm:Provider", "Fake");
        builder.UseSetting("RatingSnapshots:Interval", "00:00:01");

        if (!_quietRequests)
        {
            return;
        }

        builder.UseSetting("Arena:Requests:Mode", "Mock");
        builder.UseSetting("Arena:Requests:RequireApiKeyModels", "false");
        builder.UseSetting("Arena:Requests:Menu:0:Code", "fa");
        builder.UseSetting("Arena:Requests:Menu:0:PlayerId", "fake-a");
        builder.UseSetting("Arena:Requests:Menu:1:Code", "fb");
        builder.UseSetting("Arena:Requests:Menu:1:PlayerId", "fake-b");
        builder.ConfigureServices(services =>
        {
            foreach (var hosted in services
                .Where(d => d.ServiceType == typeof(IHostedService)
                    && (d.ImplementationType == typeof(BroadcastRunner) || d.ImplementationType == typeof(RequestRecovery)))
                .ToList())
            {
                services.Remove(hosted);
            }

            // The queue hands out nothing until the recovery has opened its gate (RequestRecoveryGate), and the recovery is
            // removed above, so the gate is open from the start here.
            var gate = new RequestRecoveryGate();
            gate.Open();
            services.AddSingleton(gate);
        });
    }

    public IDbContextFactory<ArenaDbContext> Factory => Services.GetRequiredService<IDbContextFactory<ArenaDbContext>>();

    /// <summary>Starts the host and waits for the start-up migration, so a request does not race it.</summary>
    public async Task WaitUntilReadyAsync()
    {
        var readiness = Services.GetRequiredService<DatabaseReadiness>();
        await readiness.Finished.WaitAsync(TimeSpan.FromSeconds(60));
        if (readiness.Current.State != DatabaseState.Ready)
        {
            throw new InvalidOperationException($"The database did not become ready: {readiness.Current.Error}");
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            Database.Dispose();
        }
    }
}
