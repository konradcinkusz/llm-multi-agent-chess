using ChessApi.Entities;
using Microsoft.EntityFrameworkCore;

namespace ChessApi.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Game> Games => Set<Game>();
    public DbSet<MoveRecord> Moves => Set<MoveRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Game>(e =>
        {
            e.HasKey(g => g.Id);
            e.Property(g => g.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(g => g.CreatedAt).HasDefaultValueSql("now()");
            e.HasMany(g => g.Moves)
             .WithOne(m => m.Game)
             .HasForeignKey(m => m.GameId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MoveRecord>(e =>
        {
            e.HasKey(m => m.Id);
            e.HasIndex(m => new { m.GameId, m.Ply }).IsUnique();
        });
    }
}
