using Chess.Api.Persistence;
using Chess.Api.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Chess.Api.Tests.Persistence;

/// <summary>03 §4 and §10: the usage of every attempt is a column of <c>move_attempts</c>, which the migration adds.</summary>
public class AttemptUsageMappingTests
{
    [Theory]
    [InlineData(nameof(MoveAttemptEntity.Reasoning), "reasoning")]
    [InlineData(nameof(MoveAttemptEntity.InputTokens), "input_tokens")]
    [InlineData(nameof(MoveAttemptEntity.OutputTokens), "output_tokens")]
    [InlineData(nameof(MoveAttemptEntity.ReasoningTokens), "reasoning_tokens")]
    [InlineData(nameof(MoveAttemptEntity.CachedInputTokens), "cached_input_tokens")]
    [InlineData(nameof(MoveAttemptEntity.AiCredits), "ai_credits")]
    [InlineData(nameof(MoveAttemptEntity.RejectionKind), "rejection_kind")]
    public void EachUsageFieldIsAnOptionalColumnOfMoveAttempts(string property, string column)
    {
        using var db = new ArenaDbContext(new DbContextOptionsBuilder<ArenaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var entity = db.Model.FindEntityType(typeof(MoveAttemptEntity))!;
        var field = entity.FindProperty(property)!;

        Assert.Equal("move_attempts", entity.GetTableName());
        Assert.Equal(column, field.GetColumnName());
        Assert.True(field.IsNullable);
    }

    [Fact]
    public async Task AnAttemptWithoutUsageStillRoundTripsWithItsUsageNull()
    {
        var options = new DbContextOptionsBuilder<ArenaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var gameId = Guid.NewGuid();
        await using (var write = new ArenaDbContext(options))
        {
            write.MoveAttempts.Add(new MoveAttemptEntity
            {
                GameId = gameId,
                Ply = 1,
                Number = 1,
                Prompt = "prompt",
                RawReply = "e4",
                Legal = true,
                CreatedAt = DateTimeOffset.UnixEpoch,
                InputTokens = 700,
                OutputTokens = 300,
            });
            await write.SaveChangesAsync();
        }

        await using var read = new ArenaDbContext(options);
        var loaded = await read.MoveAttempts.SingleAsync(a => a.GameId == gameId);

        Assert.Equal(700, loaded.InputTokens);
        Assert.Equal(300, loaded.OutputTokens);
        Assert.Null(loaded.Reasoning);
        Assert.Null(loaded.AiCredits);
        Assert.Null(loaded.RejectionKind);
    }
}
