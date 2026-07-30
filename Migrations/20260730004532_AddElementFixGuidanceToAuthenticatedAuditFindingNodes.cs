using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CityWebsiteAuditDashboard.Migrations
{
    /// <inheritdoc />
    public partial class AddElementFixGuidanceToAuthenticatedAuditFindingNodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ElementFixGuidance",
                table: "AuthenticatedAuditFindingNodes",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ElementFixGuidance",
                table: "AuthenticatedAuditFindingNodes");
        }
    }
}
