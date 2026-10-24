using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Chess.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tournaments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    models = table.Column<string>(type: "jsonb", nullable: false),
                    games_per_pair = table.Column<int>(type: "integer", nullable: false),
                    max_concurrency = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tournaments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "games",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tournament_id = table.Column<Guid>(type: "uuid", nullable: true),
                    white_kind = table.Column<string>(type: "text", nullable: false),
                    white_model = table.Column<string>(type: "text", nullable: false),
                    black_kind = table.Column<string>(type: "text", nullable: false),
                    black_model = table.Column<string>(type: "text", nullable: false),
                    rated = table.Column<bool>(type: "boolean", nullable: false),
                    result = table.Column<string>(type: "text", nullable: false),
                    termination = table.Column<string>(type: "text", nullable: true),
                    start_fen = table.Column<string>(type: "text", nullable: false),
                    current_fen = table.Column<string>(type: "text", nullable: false),
                    ply_count = table.Column<int>(type: "integer", nullable: false),
                    pgn = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_games", x => x.id);
                    table.ForeignKey(
                        name: "FK_games_tournaments_tournament_id",
                        column: x => x.tournament_id,
                        principalTable: "tournaments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "move_attempts",
                columns: table => new
                {
                    game_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ply = table.Column<int>(type: "integer", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    prompt = table.Column<string>(type: "text", nullable: false),
                    raw_reply = table.Column<string>(type: "text", nullable: true),
                    parsed_san = table.Column<string>(type: "text", nullable: true),
                    legal = table.Column<bool>(type: "boolean", nullable: false),
                    latency_ms = table.Column<long>(type: "bigint", nullable: false),
                    error = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_move_attempts", x => new { x.game_id, x.ply, x.number });
                    table.ForeignKey(
                        name: "FK_move_attempts_games_game_id",
                        column: x => x.game_id,
                        principalTable: "games",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "moves",
                columns: table => new
                {
                    game_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ply = table.Column<int>(type: "integer", nullable: false),
                    color = table.Column<string>(type: "text", nullable: false),
                    san = table.Column<string>(type: "text", nullable: false),
                    fen_after = table.Column<string>(type: "text", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    latency_ms = table.Column<long>(type: "bigint", nullable: false),
                    input_tokens = table.Column<int>(type: "integer", nullable: true),
                    output_tokens = table.Column<int>(type: "integer", nullable: true),
                    centipawn_loss = table.Column<int>(type: "integer", nullable: true),
                    best_san = table.Column<string>(type: "text", nullable: true),
                    played_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_moves", x => new { x.game_id, x.ply });
                    table.ForeignKey(
                        name: "FK_moves_games_game_id",
                        column: x => x.game_id,
                        principalTable: "games",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rating_snapshots",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    taken_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    after_game_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rated_games = table.Column<int>(type: "integer", nullable: false),
                    model = table.Column<string>(type: "text", nullable: false),
                    rating = table.Column<double>(type: "double precision", nullable: false),
                    ci_low = table.Column<double>(type: "double precision", nullable: false),
                    ci_high = table.Column<double>(type: "double precision", nullable: false),
                    arena_score = table.Column<double>(type: "double precision", nullable: false),
                    games = table.Column<int>(type: "integer", nullable: false),
                    provisional = table.Column<bool>(type: "boolean", nullable: false),
                    formula_version = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rating_snapshots", x => x.id);
                    table.ForeignKey(
                        name: "FK_rating_snapshots_games_after_game_id",
                        column: x => x.after_game_id,
                        principalTable: "games",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_games_tournament_id",
                table: "games",
                column: "tournament_id");

            migrationBuilder.CreateIndex(
                name: "IX_rating_snapshots_after_game_id",
                table: "rating_snapshots",
                column: "after_game_id");

            migrationBuilder.CreateIndex(
                name: "IX_rating_snapshots_model_taken_at",
                table: "rating_snapshots",
                columns: new[] { "model", "taken_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "move_attempts");

            migrationBuilder.DropTable(
                name: "moves");

            migrationBuilder.DropTable(
                name: "rating_snapshots");

            migrationBuilder.DropTable(
                name: "games");

            migrationBuilder.DropTable(
                name: "tournaments");
        }
    }
}
