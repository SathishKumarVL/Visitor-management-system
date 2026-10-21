using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tiaano.Vms.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class OpenVisitsTakeHostLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Open visits (expected, awaiting approval, approved, inside) saved without a location were hidden
            // from location-bound logins, the host's included; give them the host's active location.
            migrationBuilder.Sql("""
                INSERT INTO VisitorVisitLocations (VisitorVisitId, LocationId)
                SELECT v.Id, e.LocationId
                FROM VisitorVisits v
                JOIN Employees e ON e.Id = v.HostEmployeeId
                JOIN Locations l ON l.Id = e.LocationId AND l.IsActive = 1
                WHERE v.Status IN (0, 1, 2, 5)
                  AND e.LocationId IS NOT NULL
                  AND NOT EXISTS (SELECT 1 FROM VisitorVisitLocations vl WHERE vl.VisitorVisitId = v.Id);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
