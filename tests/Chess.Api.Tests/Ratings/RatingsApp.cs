using Chess.Api.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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
