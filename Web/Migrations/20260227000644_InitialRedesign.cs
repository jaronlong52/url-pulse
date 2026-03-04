using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace UrlPulse.Migrations
{
    /// <inheritdoc />
    public partial class InitialRedesign : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastChecked",
                table: "UrlMonitors");

            migrationBuilder.DropColumn(
                name: "LatencyMs",
                table: "UrlMonitors");

            migrationBuilder.RenameColumn(
                name: "IsUp",
                table: "UrlMonitors",
                newName: "IsActive");

            migrationBuilder.AddColumn<int>(
                name: "CheckIntervalSeconds",
                table: "UrlMonitors",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TimeoutMs",
                table: "UrlMonitors",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "LatencyHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LatencyMs = table.Column<int>(type: "integer", nullable: false),
                    StatusCode = table.Column<int>(type: "integer", nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: false),
                    CheckedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UrlMonitorId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LatencyHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LatencyHistories_UrlMonitors_UrlMonitorId",
                        column: x => x.UrlMonitorId,
                        principalTable: "UrlMonitors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LatencyHistories_CheckedAt",
                table: "LatencyHistories",
                column: "CheckedAt");

            migrationBuilder.CreateIndex(
                name: "IX_LatencyHistory_Monitor_Date",
                table: "LatencyHistories",
                columns: new[] { "UrlMonitorId", "CheckedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LatencyHistories");

            migrationBuilder.DropColumn(
                name: "CheckIntervalSeconds",
                table: "UrlMonitors");

            migrationBuilder.DropColumn(
                name: "TimeoutMs",
                table: "UrlMonitors");

            migrationBuilder.RenameColumn(
                name: "IsActive",
                table: "UrlMonitors",
                newName: "IsUp");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastChecked",
                table: "UrlMonitors",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LatencyMs",
                table: "UrlMonitors",
                type: "integer",
                nullable: true);
        }
    }
}
