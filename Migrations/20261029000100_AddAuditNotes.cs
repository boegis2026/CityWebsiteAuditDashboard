using CityWebsiteAuditDashboard.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CityWebsiteAuditDashboard.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261029000100_AddAuditNotes")]
public class AddAuditNotes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "AuditNotes", table: "AuthenticatedAuditRuns",
            type: "nvarchar(4000)", maxLength: 4000, nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "AuditNotes", table: "AuthenticatedAuditRuns");
    }
}
