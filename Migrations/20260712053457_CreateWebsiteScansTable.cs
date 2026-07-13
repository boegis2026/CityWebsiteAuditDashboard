using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CityWebsiteAuditDashboard.Migrations
{
    /// <inheritdoc />
    public partial class CreateWebsiteScansTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WebsiteScans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Url = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    HttpStatusCode = table.Column<int>(type: "int", nullable: true),
                    ResponseTimeMilliseconds = table.Column<long>(type: "bigint", nullable: true),
                    ServerHeader = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    XPoweredByHeader = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IPv4Address = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DateScanned = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebsiteScans", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WebsiteScans");
        }
    }
}
