using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CityWebsiteAuditDashboard.Migrations
{
    /// <inheritdoc />
    public partial class PreserveRemediationAuditEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AccessibilityRemediationFindingOccurrences_AuthenticatedAuditFindings_AuthenticatedAuditFindingId",
                table: "AccessibilityRemediationFindingOccurrences");

            migrationBuilder.AddForeignKey(
                name: "FK_AccessibilityRemediationFindingOccurrences_AuthenticatedAuditFindings_AuthenticatedAuditFindingId",
                table: "AccessibilityRemediationFindingOccurrences",
                column: "AuthenticatedAuditFindingId",
                principalTable: "AuthenticatedAuditFindings",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AccessibilityRemediationFindingOccurrences_AuthenticatedAuditFindings_AuthenticatedAuditFindingId",
                table: "AccessibilityRemediationFindingOccurrences");

            migrationBuilder.AddForeignKey(
                name: "FK_AccessibilityRemediationFindingOccurrences_AuthenticatedAuditFindings_AuthenticatedAuditFindingId",
                table: "AccessibilityRemediationFindingOccurrences",
                column: "AuthenticatedAuditFindingId",
                principalTable: "AuthenticatedAuditFindings",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
