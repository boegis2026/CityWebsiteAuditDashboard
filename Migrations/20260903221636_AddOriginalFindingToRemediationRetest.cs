using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CityWebsiteAuditDashboard.Migrations
{
    /// <inheritdoc />
    public partial class AddOriginalFindingToRemediationRetest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "OriginalAuthenticatedAuditFindingId",
                table: "AccessibilityRemediationRetests",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccessibilityRemediationRetests_OriginalAuthenticatedAuditFindingId",
                table: "AccessibilityRemediationRetests",
                column: "OriginalAuthenticatedAuditFindingId");

            migrationBuilder.AddForeignKey(
                name: "FK_AccessibilityRemediationRetests_AuthenticatedAuditFindings_OriginalAuthenticatedAuditFindingId",
                table: "AccessibilityRemediationRetests",
                column: "OriginalAuthenticatedAuditFindingId",
                principalTable: "AuthenticatedAuditFindings",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AccessibilityRemediationRetests_AuthenticatedAuditFindings_OriginalAuthenticatedAuditFindingId",
                table: "AccessibilityRemediationRetests");

            migrationBuilder.DropIndex(
                name: "IX_AccessibilityRemediationRetests_OriginalAuthenticatedAuditFindingId",
                table: "AccessibilityRemediationRetests");

            migrationBuilder.DropColumn(
                name: "OriginalAuthenticatedAuditFindingId",
                table: "AccessibilityRemediationRetests");
        }
    }
}
