using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CityWebsiteAuditDashboard.Migrations
{
    /// <inheritdoc />
    public partial class AddRemediationRetestType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RetestType",
                table: "AccessibilityRemediationRetests",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "CurrentState");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RetestType",
                table: "AccessibilityRemediationRetests");
        }
    }
}
