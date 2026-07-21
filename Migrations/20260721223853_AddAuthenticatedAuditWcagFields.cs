using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CityWebsiteAuditDashboard.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthenticatedAuditWcagFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "WcagLevel",
                table: "AuthenticatedAuditFindings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WcagTags",
                table: "AuthenticatedAuditFindings",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WcagLevel",
                table: "AuthenticatedAuditFindings");

            migrationBuilder.DropColumn(
                name: "WcagTags",
                table: "AuthenticatedAuditFindings");
        }
    }
}
