using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CityWebsiteAuditDashboard.Migrations
{
    /// <inheritdoc />
    public partial class AddAccessibilityRemediationTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccessibilityRemediationItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    AssignedTo = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessibilityRemediationItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AccessibilityRemediationFindingOccurrences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AccessibilityRemediationItemId = table.Column<int>(type: "int", nullable: false),
                    AuthenticatedAuditFindingId = table.Column<int>(type: "int", nullable: false),
                    MatchMethod = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    MatchConfidence = table.Column<decimal>(type: "decimal(5,4)", precision: 5, scale: 4, nullable: true),
                    LinkedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LinkedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessibilityRemediationFindingOccurrences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccessibilityRemediationFindingOccurrences_AccessibilityRemediationItems_AccessibilityRemediationItemId",
                        column: x => x.AccessibilityRemediationItemId,
                        principalTable: "AccessibilityRemediationItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AccessibilityRemediationFindingOccurrences_AuthenticatedAuditFindings_AuthenticatedAuditFindingId",
                        column: x => x.AuthenticatedAuditFindingId,
                        principalTable: "AuthenticatedAuditFindings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AccessibilityRemediationHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AccessibilityRemediationItemId = table.Column<int>(type: "int", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PreviousStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    NewStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    PreviousAssignee = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    NewAssignee = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ChangedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ChangedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessibilityRemediationHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccessibilityRemediationHistories_AccessibilityRemediationItems_AccessibilityRemediationItemId",
                        column: x => x.AccessibilityRemediationItemId,
                        principalTable: "AccessibilityRemediationItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccessibilityRemediationFindingOccurrences_AccessibilityRemediationItemId",
                table: "AccessibilityRemediationFindingOccurrences",
                column: "AccessibilityRemediationItemId");

            migrationBuilder.CreateIndex(
                name: "IX_AccessibilityRemediationFindingOccurrences_AuthenticatedAuditFindingId",
                table: "AccessibilityRemediationFindingOccurrences",
                column: "AuthenticatedAuditFindingId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccessibilityRemediationHistories_AccessibilityRemediationItemId",
                table: "AccessibilityRemediationHistories",
                column: "AccessibilityRemediationItemId");

            migrationBuilder.CreateIndex(
                name: "IX_AccessibilityRemediationHistories_ChangedAt",
                table: "AccessibilityRemediationHistories",
                column: "ChangedAt");

            migrationBuilder.CreateIndex(
                name: "IX_AccessibilityRemediationItems_AssignedTo",
                table: "AccessibilityRemediationItems",
                column: "AssignedTo");

            migrationBuilder.CreateIndex(
                name: "IX_AccessibilityRemediationItems_Status",
                table: "AccessibilityRemediationItems",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccessibilityRemediationFindingOccurrences");

            migrationBuilder.DropTable(
                name: "AccessibilityRemediationHistories");

            migrationBuilder.DropTable(
                name: "AccessibilityRemediationItems");
        }
    }
}
