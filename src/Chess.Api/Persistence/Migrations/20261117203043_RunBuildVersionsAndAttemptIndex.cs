using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChessApi.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RunBuildVersionsAndAttemptIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "app_version",
                table: "tournaments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "formula_version",
                table: "tournaments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "prompt_version",
                table: "tournaments",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_move_attempts_created_at_game_id_ply_number",
                table: "move_attempts",
                columns: new[] { "created_at", "game_id", "ply", "number" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_move_attempts_created_at_game_id_ply_number",
                table: "move_attempts");

            migrationBuilder.DropColumn(
                name: "app_version",
                table: "tournaments");

            migrationBuilder.DropColumn(
                name: "formula_version",
                table: "tournaments");

            migrationBuilder.DropColumn(
                name: "prompt_version",
                table: "tournaments");
        }
    }
}
