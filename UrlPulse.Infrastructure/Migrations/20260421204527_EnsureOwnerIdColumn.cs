using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UrlPulse.Core.Migrations
{
    /// <inheritdoc />
    public partial class EnsureOwnerIdColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                        name: "OwnerId",
                        table: "UrlMonitors",
                        type: "text",
                        nullable: false,
                        defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                        name: "OwnerId",
                        table: "UrlMonitors");
        }
    }
}
