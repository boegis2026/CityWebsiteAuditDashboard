using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CityWebsiteAuditDashboard.Migrations
{
    /// <inheritdoc />
    public partial class AddRemediationRetestAuditRun : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AuthenticatedAuditRunId",
                table: "AccessibilityRemediationRetests",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccessibilityRemediationRetests_AuthenticatedAuditRunId",
                table: "AccessibilityRemediationRetests",
                column: "AuthenticatedAuditRunId");

            migrationBuilder.AddForeignKey(
                name: "FK_AccessibilityRemediationRetests_AuthenticatedAuditRuns_AuthenticatedAuditRunId",
                table: "AccessibilityRemediationRetests",
                column: "AuthenticatedAuditRunId",
                principalTable: "AuthenticatedAuditRuns",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AccessibilityRemediationRetests_AuthenticatedAuditRuns_AuthenticatedAuditRunId",
                table: "AccessibilityRemediationRetests");

            migrationBuilder.DropIndex(
                name: "IX_AccessibilityRemediationRetests_AuthenticatedAuditRunId",
                table: "AccessibilityRemediationRetests");

            migrationBuilder.DropColumn(
                name: "AuthenticatedAuditRunId",
                table: "AccessibilityRemediationRetests");
        }
    }
}
