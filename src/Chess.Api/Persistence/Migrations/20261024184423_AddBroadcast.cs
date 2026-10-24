using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChessApi.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBroadcast : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "broadcast_schedules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    move_delay_ms = table.Column<int>(type: "integer", nullable: false),
                    game_gap_seconds = table.Column<int>(type: "integer", nullable: false),
                    seed = table.Column<int>(type: "integer", nullable: false),
                    models = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_broadcast_schedules", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "broadcast_slots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    schedule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    index = table.Column<int>(type: "integer", nullable: false),
                    white_kind = table.Column<string>(type: "text", nullable: false),
                    white_model = table.Column<string>(type: "text", nullable: false),
                    black_kind = table.Column<string>(type: "text", nullable: false),
                    black_model = table.Column<string>(type: "text", nullable: false),
                    planned_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    planned_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    game_id = table.Column<Guid>(type: "uuid", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_broadcast_slots", x => x.id);
                    table.ForeignKey(
                        name: "FK_broadcast_slots_broadcast_schedules_schedule_id",
                        column: x => x.schedule_id,
                        principalTable: "broadcast_schedules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_broadcast_slots_games_game_id",
                        column: x => x.game_id,
                        principalTable: "games",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_broadcast_slots_game_id",
                table: "broadcast_slots",
                column: "game_id");

            migrationBuilder.CreateIndex(
                name: "IX_broadcast_slots_schedule_id_index",
                table: "broadcast_slots",
                columns: new[] { "schedule_id", "index" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "broadcast_slots");

            migrationBuilder.DropTable(
                name: "broadcast_schedules");
        }
    }
}
