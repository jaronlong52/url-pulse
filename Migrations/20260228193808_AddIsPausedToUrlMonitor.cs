using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UrlPulse.Migrations
{
    /// <inheritdoc />
    public partial class AddIsPausedToUrlMonitor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPaused",
                table: "UrlMonitors",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsPaused",
                table: "UrlMonitors");
        }
    }
}
