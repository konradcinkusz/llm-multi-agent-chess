using Chess.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Chess.Api.Tests.Quality;

/// <summary>Hands out a fresh InMemory context per call, as the production factory does.</summary>
internal sealed class QualityFactory(string databaseName) : IDbContextFactory<ArenaDbContext>
{
    public ArenaDbContext CreateDbContext() => QualitySeed.NewContext(databaseName);
}
