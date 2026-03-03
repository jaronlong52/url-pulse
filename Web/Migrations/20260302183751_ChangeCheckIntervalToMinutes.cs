using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UrlPulse.Migrations
{
    /// <inheritdoc />
    public partial class ChangeCheckIntervalToMinutes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "CheckIntervalSeconds",
                table: "UrlMonitors",
                newName: "CheckIntervalMinutes");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "CheckIntervalMinutes",
                table: "UrlMonitors",
                newName: "CheckIntervalSeconds");
        }
    }
}
