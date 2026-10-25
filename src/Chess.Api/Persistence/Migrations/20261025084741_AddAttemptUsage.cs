using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChessApi.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAttemptUsage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "ai_credits",
                table: "move_attempts",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "cached_input_tokens",
                table: "move_attempts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "input_tokens",
                table: "move_attempts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "output_tokens",
                table: "move_attempts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reasoning",
                table: "move_attempts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "reasoning_tokens",
                table: "move_attempts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rejection_kind",
                table: "move_attempts",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ai_credits",
                table: "move_attempts");

            migrationBuilder.DropColumn(
                name: "cached_input_tokens",
                table: "move_attempts");

            migrationBuilder.DropColumn(
                name: "input_tokens",
                table: "move_attempts");

            migrationBuilder.DropColumn(
                name: "output_tokens",
                table: "move_attempts");

            migrationBuilder.DropColumn(
                name: "reasoning",
                table: "move_attempts");

            migrationBuilder.DropColumn(
                name: "reasoning_tokens",
                table: "move_attempts");

            migrationBuilder.DropColumn(
                name: "rejection_kind",
                table: "move_attempts");
        }
    }
}
