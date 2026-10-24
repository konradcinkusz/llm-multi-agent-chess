using Chess.Api.Persistence;
using Chess.Api.Results;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Chess.Api.Tests.Ratings;

/// <summary>The app on its own InMemory store, which nothing else shares.</summary>
public class RatingsApp : WebApplicationFactory<Program>
{
    public string DatabaseName { get; } = Guid.NewGuid().ToString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Database:Provider", "InMemory");
        builder.UseSetting("Database:InMemoryName", DatabaseName);
        builder.UseSetting("Llm:Provider", "Fake");
        builder.ConfigureTestServices(services =>
        {
            // The tests write to the store after startup, so nothing may play or schedule games in it. Otherwise the game
            // runner races the seed: it requeues the seeded queued rows if the seed lands first, and plays them to the end.
            // The schema and the rating snapshots still run, as they do in the real app.
            services.RemoveAll<IHostedService>();
            services.AddHostedService<MigrationHostedService>();
            services.AddHostedService<RatingSnapshotService>();
        });
    }

    public void Seed(Action<ArenaDbContext> seed)
    {
        var factory = Services.GetRequiredService<IDbContextFactory<ArenaDbContext>>();
        using var db = factory.CreateDbContext();
        seed(db);
        db.SaveChanges();
    }
}

/// <summary>The app with <see cref="RatingSeed"/> already in its store.</summary>
public sealed class SeededRatingsApp : RatingsApp
{
    public SeededRatingsApp() => Seed(RatingSeed.Populate);
}
