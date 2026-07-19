using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CityWebsiteAuditDashboard.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthenticatedAuditFindings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuthenticatedAuditFindings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AuthenticatedAuditStepId = table.Column<int>(type: "int", nullable: false),
                    FindingType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    RuleId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Impact = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Help = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    HelpUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    AffectedElementCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthenticatedAuditFindings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuthenticatedAuditFindings_AuthenticatedAuditSteps_AuthenticatedAuditStepId",
                        column: x => x.AuthenticatedAuditStepId,
                        principalTable: "AuthenticatedAuditSteps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuthenticatedAuditFindings_AuthenticatedAuditStepId_FindingType_RuleId",
                table: "AuthenticatedAuditFindings",
                columns: new[] { "AuthenticatedAuditStepId", "FindingType", "RuleId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuthenticatedAuditFindings");
        }
    }
}
