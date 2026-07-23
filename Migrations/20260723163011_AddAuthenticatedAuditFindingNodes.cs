using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CityWebsiteAuditDashboard.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthenticatedAuditFindingNodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuthenticatedAuditFindingNodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AuthenticatedAuditFindingId = table.Column<int>(type: "int", nullable: false),
                    Target = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Html = table.Column<string>(type: "nvarchar(max)", maxLength: 10000, nullable: true),
                    FailureSummary = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthenticatedAuditFindingNodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuthenticatedAuditFindingNodes_AuthenticatedAuditFindings_AuthenticatedAuditFindingId",
                        column: x => x.AuthenticatedAuditFindingId,
                        principalTable: "AuthenticatedAuditFindings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuthenticatedAuditFindingNodes_AuthenticatedAuditFindingId",
                table: "AuthenticatedAuditFindingNodes",
                column: "AuthenticatedAuditFindingId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuthenticatedAuditFindingNodes");
        }
    }
}
