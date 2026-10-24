using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Chess.Api.Persistence;

/// <summary>
/// Design-time only: lets <c>dotnet ef migrations</c> build the model without starting the host.
/// No connection is opened by <c>migrations add</c> or <c>migrations script</c>.
/// </summary>
public sealed class ArenaDbContextFactory : IDesignTimeDbContextFactory<ArenaDbContext>
{
    public ArenaDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ArenaDbContext>()
            .UseNpgsql("Host=localhost;Database=chess")
            .Options;
        return new ArenaDbContext(options);
    }
}
