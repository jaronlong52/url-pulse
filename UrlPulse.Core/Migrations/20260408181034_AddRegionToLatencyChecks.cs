using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UrlPulse.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddRegionToLatencyChecks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Region",
                table: "LatencyHistories",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Region",
                table: "LatencyHistories");
        }
    }
}
