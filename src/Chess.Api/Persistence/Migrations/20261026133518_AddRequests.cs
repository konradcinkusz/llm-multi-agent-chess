using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChessApi.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "game_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false),
                    external_id = table.Column<string>(type: "text", nullable: false),
                    payer_id = table.Column<string>(type: "text", nullable: false),
                    payer_display = table.Column<string>(type: "text", nullable: false),
                    white = table.Column<string>(type: "text", nullable: false),
                    black = table.Column<string>(type: "text", nullable: false),
                    price_micros = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: true),
                    game_id = table.Column<Guid>(type: "uuid", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_game_requests", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "payer_ledger",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payer_id = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    amount_micros = table.Column<long>(type: "bigint", nullable: false),
                    external_id = table.Column<string>(type: "text", nullable: false),
                    currency = table.Column<string>(type: "text", nullable: true),
                    reason = table.Column<string>(type: "text", nullable: true),
                    request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payer_ledger", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_game_requests_external_id",
                table: "game_requests",
                column: "external_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_game_requests_status_received_at",
                table: "game_requests",
                columns: new[] { "status", "received_at" });

            migrationBuilder.CreateIndex(
                name: "IX_payer_ledger_external_id",
                table: "payer_ledger",
                column: "external_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payer_ledger_payer_id",
                table: "payer_ledger",
                column: "payer_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "game_requests");

            migrationBuilder.DropTable(
                name: "payer_ledger");
        }
    }
}
