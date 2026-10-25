using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChessApi.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBenchmarkKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "candidate",
                table: "tournaments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "kind",
                table: "tournaments",
                type: "text",
                nullable: false,
                defaultValue: "round_robin");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "candidate",
                table: "tournaments");

            migrationBuilder.DropColumn(
                name: "kind",
                table: "tournaments");
        }
    }
}
