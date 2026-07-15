using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CityWebsiteAuditDashboard.Migrations
{
    /// <inheritdoc />
    public partial class AddWaveAccessibilityFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "WaveAlerts",
                table: "WebsiteScans",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WaveAria",
                table: "WebsiteScans",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WaveContrastErrors",
                table: "WebsiteScans",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WaveErrorMessage",
                table: "WebsiteScans",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WaveErrors",
                table: "WebsiteScans",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WaveFeatures",
                table: "WebsiteScans",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "WaveScanSucceeded",
                table: "WebsiteScans",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "WaveScannedAt",
                table: "WebsiteScans",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WaveAlerts",
                table: "WebsiteScans");

            migrationBuilder.DropColumn(
                name: "WaveAria",
                table: "WebsiteScans");

            migrationBuilder.DropColumn(
                name: "WaveContrastErrors",
                table: "WebsiteScans");

            migrationBuilder.DropColumn(
                name: "WaveErrorMessage",
                table: "WebsiteScans");

            migrationBuilder.DropColumn(
                name: "WaveErrors",
                table: "WebsiteScans");

            migrationBuilder.DropColumn(
                name: "WaveFeatures",
                table: "WebsiteScans");

            migrationBuilder.DropColumn(
                name: "WaveScanSucceeded",
                table: "WebsiteScans");

            migrationBuilder.DropColumn(
                name: "WaveScannedAt",
                table: "WebsiteScans");
        }
    }
}
