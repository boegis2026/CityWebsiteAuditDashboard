using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CityWebsiteAuditDashboard.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthenticatedAuditTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuthenticatedAuditRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ApplicationName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    StartingUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    AccessibilityEngine = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthenticatedAuditRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuthenticatedAuditSteps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AuthenticatedAuditRunId = table.Column<int>(type: "int", nullable: false),
                    StepNumber = table.Column<int>(type: "int", nullable: false),
                    StepName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Url = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    PageTitle = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Heading = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DomFingerprint = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ScannedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    VisibleFormCount = table.Column<int>(type: "int", nullable: false),
                    VisibleFieldCount = table.Column<int>(type: "int", nullable: false),
                    VisibleButtonCount = table.Column<int>(type: "int", nullable: false),
                    ViolationRuleCount = table.Column<int>(type: "int", nullable: false),
                    AffectedElementCount = table.Column<int>(type: "int", nullable: false),
                    NeedsReviewRuleCount = table.Column<int>(type: "int", nullable: false),
                    PassedRuleCount = table.Column<int>(type: "int", nullable: false),
                    ScanSucceeded = table.Column<bool>(type: "bit", nullable: false),
                    WasFinalStep = table.Column<bool>(type: "bit", nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthenticatedAuditSteps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuthenticatedAuditSteps_AuthenticatedAuditRuns_AuthenticatedAuditRunId",
                        column: x => x.AuthenticatedAuditRunId,
                        principalTable: "AuthenticatedAuditRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuthenticatedAuditRuns_StartedAt",
                table: "AuthenticatedAuditRuns",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_AuthenticatedAuditRuns_Status",
                table: "AuthenticatedAuditRuns",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_AuthenticatedAuditSteps_AuthenticatedAuditRunId_StepNumber",
                table: "AuthenticatedAuditSteps",
                columns: new[] { "AuthenticatedAuditRunId", "StepNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuthenticatedAuditSteps_ScannedAt",
                table: "AuthenticatedAuditSteps",
                column: "ScannedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuthenticatedAuditSteps");

            migrationBuilder.DropTable(
                name: "AuthenticatedAuditRuns");
        }
    }
}
