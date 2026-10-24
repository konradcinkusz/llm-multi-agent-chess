using System.Text.Json;
using Chess.Api.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Chess.Api.Persistence;

public sealed class ArenaDbContext(DbContextOptions<ArenaDbContext> options) : DbContext(options)
{
    private static readonly ValueConverter<List<string>, string> ModelsConverter = new(
        v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
        v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>());

    private static readonly ValueComparer<List<string>> ModelsComparer = new(
        (a, b) => a!.SequenceEqual(b!),
        v => v.Aggregate(0, (hash, item) => HashCode.Combine(hash, item)),
        v => v.ToList());

    public DbSet<GameEntity> Games => Set<GameEntity>();
    public DbSet<MoveEntity> Moves => Set<MoveEntity>();
    public DbSet<MoveAttemptEntity> MoveAttempts => Set<MoveAttemptEntity>();
    public DbSet<TournamentEntity> Tournaments => Set<TournamentEntity>();
    public DbSet<RatingSnapshotEntity> RatingSnapshots => Set<RatingSnapshotEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GameEntity>(e =>
        {
            e.ToTable("games");
            e.HasKey(g => g.Id);
            e.HasIndex(g => g.TournamentId);
            e.HasOne<TournamentEntity>().WithMany().HasForeignKey(g => g.TournamentId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<MoveEntity>(e =>
        {
            e.ToTable("moves");
            e.HasKey(m => new { m.GameId, m.Ply });
            e.HasOne<GameEntity>().WithMany().HasForeignKey(m => m.GameId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MoveAttemptEntity>(e =>
        {
            e.ToTable("move_attempts");
            e.HasKey(a => new { a.GameId, a.Ply, a.Number });
            e.HasOne<GameEntity>().WithMany().HasForeignKey(a => a.GameId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TournamentEntity>(e =>
        {
            e.ToTable("tournaments");
            e.HasKey(t => t.Id);
            e.Property(t => t.Models)
                .HasConversion(ModelsConverter, ModelsComparer)
                .HasColumnType("jsonb");
        });

        modelBuilder.Entity<RatingSnapshotEntity>(e =>
        {
            e.ToTable("rating_snapshots");
            e.HasKey(s => s.Id);
            e.HasIndex(s => new { s.Model, s.TakenAt });
            e.HasOne<GameEntity>().WithMany().HasForeignKey(s => s.AfterGameId).OnDelete(DeleteBehavior.Restrict);
        });

        // Column names are snake_case (03 §4) without a third-party naming package.
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.Name));
            }
        }
    }

    private static string ToSnakeCase(string name) =>
        string.Concat(name.Select((c, i) => i > 0 && char.IsUpper(c)
            ? "_" + char.ToLowerInvariant(c)
            : char.ToLowerInvariant(c).ToString()));
}
