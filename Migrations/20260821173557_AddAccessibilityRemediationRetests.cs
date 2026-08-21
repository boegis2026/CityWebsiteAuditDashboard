using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CityWebsiteAuditDashboard.Migrations
{
    /// <inheritdoc />
    public partial class AddAccessibilityRemediationRetests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccessibilityRemediationRetests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AccessibilityRemediationItemId = table.Column<int>(type: "int", nullable: false),
                    AuthenticatedAuditStepId = table.Column<int>(type: "int", nullable: true),
                    MatchedAuthenticatedAuditFindingId = table.Column<int>(type: "int", nullable: true),
                    Result = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    MatchMethod = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    MatchConfidence = table.Column<decimal>(type: "decimal(5,4)", precision: 5, scale: 4, nullable: true),
                    RetestedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    RetestedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessibilityRemediationRetests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccessibilityRemediationRetests_AccessibilityRemediationItems_AccessibilityRemediationItemId",
                        column: x => x.AccessibilityRemediationItemId,
                        principalTable: "AccessibilityRemediationItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AccessibilityRemediationRetests_AuthenticatedAuditFindings_MatchedAuthenticatedAuditFindingId",
                        column: x => x.MatchedAuthenticatedAuditFindingId,
                        principalTable: "AuthenticatedAuditFindings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccessibilityRemediationRetests_AuthenticatedAuditSteps_AuthenticatedAuditStepId",
                        column: x => x.AuthenticatedAuditStepId,
                        principalTable: "AuthenticatedAuditSteps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccessibilityRemediationRetests_AccessibilityRemediationItemId",
                table: "AccessibilityRemediationRetests",
                column: "AccessibilityRemediationItemId");

            migrationBuilder.CreateIndex(
                name: "IX_AccessibilityRemediationRetests_AuthenticatedAuditStepId",
                table: "AccessibilityRemediationRetests",
                column: "AuthenticatedAuditStepId");

            migrationBuilder.CreateIndex(
                name: "IX_AccessibilityRemediationRetests_MatchedAuthenticatedAuditFindingId",
                table: "AccessibilityRemediationRetests",
                column: "MatchedAuthenticatedAuditFindingId");

            migrationBuilder.CreateIndex(
                name: "IX_AccessibilityRemediationRetests_Result",
                table: "AccessibilityRemediationRetests",
                column: "Result");

            migrationBuilder.CreateIndex(
                name: "IX_AccessibilityRemediationRetests_RetestedAt",
                table: "AccessibilityRemediationRetests",
                column: "RetestedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccessibilityRemediationRetests");
        }
    }
}
