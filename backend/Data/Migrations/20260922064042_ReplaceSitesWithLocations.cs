using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tiaano.Vms.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceSitesWithLocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop FKs first so we can remount AspNetUsers.SiteId onto Locations.
            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUsers_Sites_SiteId",
                table: "AspNetUsers");

            migrationBuilder.DropForeignKey(
                name: "FK_Locations_Sites_SiteId",
                table: "Locations");

            migrationBuilder.DropForeignKey(
                name: "FK_PassNumberSeries_Sites_SiteId",
                table: "PassNumberSeries");

            migrationBuilder.DropForeignKey(
                name: "FK_VisitorVisits_Sites_SiteId",
                table: "VisitorVisits");

            // Backfill: one Location per Site (same tenant+name), then remap users and visits.
            migrationBuilder.Sql("""
                -- Create a Location for each Site that does not already have a matching name.
                INSERT INTO Locations (Id, TenantId, Name, RequiresPlantNumber, RequiresOtherText, IsActive, SortOrder, CreatedAt)
                SELECT NEWID(), s.TenantId, s.Name, 0, 0, 1, 0, SYSUTCDATETIME()
                FROM Sites s
                WHERE NOT EXISTS (
                    SELECT 1 FROM Locations l
                    WHERE l.TenantId = s.TenantId AND l.Name = s.Name
                );

                -- Remap user SiteId values to the Location created/found for that Site name.
                UPDATE u
                SET u.SiteId = loc.Id
                FROM AspNetUsers u
                INNER JOIN Sites s ON s.Id = u.SiteId
                INNER JOIN Locations loc ON loc.TenantId = s.TenantId AND loc.Name = s.Name;

                -- Ensure visits that were site-scoped get a VisitLocations row when they have none.
                INSERT INTO VisitorVisitLocations (VisitorVisitId, LocationId)
                SELECT v.Id, loc.Id
                FROM VisitorVisits v
                INNER JOIN Sites s ON s.Id = v.SiteId
                INNER JOIN Locations loc ON loc.TenantId = s.TenantId AND loc.Name = s.Name
                WHERE NOT EXISTS (
                    SELECT 1 FROM VisitorVisitLocations vl WHERE vl.VisitorVisitId = v.Id
                );
                """);

            migrationBuilder.DropIndex(
                name: "IX_VisitorVisits_SiteId",
                table: "VisitorVisits");

            migrationBuilder.DropIndex(
                name: "IX_VisitorVisits_TenantId_SiteId_Status",
                table: "VisitorVisits");

            migrationBuilder.DropIndex(
                name: "IX_PassNumberSeries_SiteId",
                table: "PassNumberSeries");

            migrationBuilder.DropIndex(
                name: "IX_Locations_SiteId",
                table: "Locations");

            migrationBuilder.DropIndex(
                name: "IX_Locations_TenantId_SiteId",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "VisitorVisits");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "PassNumberSeries");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "Locations");

            migrationBuilder.DropTable(
                name: "Sites");

            migrationBuilder.RenameColumn(
                name: "MaxSites",
                table: "TenantLicenses",
                newName: "MaxLocations");

            migrationBuilder.RenameColumn(
                name: "SiteId",
                table: "AspNetUsers",
                newName: "LocationId");

            migrationBuilder.RenameIndex(
                name: "IX_AspNetUsers_SiteId",
                table: "AspNetUsers",
                newName: "IX_AspNetUsers_LocationId");

            // Clear any LocationId that no longer points at a Location (orphan after remap).
            migrationBuilder.Sql("""
                UPDATE AspNetUsers
                SET LocationId = NULL
                WHERE LocationId IS NOT NULL
                  AND NOT EXISTS (SELECT 1 FROM Locations l WHERE l.Id = AspNetUsers.LocationId);
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUsers_Locations_LocationId",
                table: "AspNetUsers",
                column: "LocationId",
                principalTable: "Locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUsers_Locations_LocationId",
                table: "AspNetUsers");

            migrationBuilder.RenameColumn(
                name: "MaxLocations",
                table: "TenantLicenses",
                newName: "MaxSites");

            migrationBuilder.RenameColumn(
                name: "LocationId",
                table: "AspNetUsers",
                newName: "SiteId");

            migrationBuilder.RenameIndex(
                name: "IX_AspNetUsers_LocationId",
                table: "AspNetUsers",
                newName: "IX_AspNetUsers_SiteId");

            migrationBuilder.AddColumn<Guid>(
                name: "SiteId",
                table: "VisitorVisits",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SiteId",
                table: "PassNumberSeries",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SiteId",
                table: "Locations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Sites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Address = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sites", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Sites_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VisitorVisits_SiteId",
                table: "VisitorVisits",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_VisitorVisits_TenantId_SiteId_Status",
                table: "VisitorVisits",
                columns: new[] { "TenantId", "SiteId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PassNumberSeries_SiteId",
                table: "PassNumberSeries",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_Locations_SiteId",
                table: "Locations",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_Locations_TenantId_SiteId",
                table: "Locations",
                columns: new[] { "TenantId", "SiteId" });

            migrationBuilder.CreateIndex(
                name: "IX_Sites_TenantId_Code",
                table: "Sites",
                columns: new[] { "TenantId", "Code" });

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUsers_Sites_SiteId",
                table: "AspNetUsers",
                column: "SiteId",
                principalTable: "Sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Locations_Sites_SiteId",
                table: "Locations",
                column: "SiteId",
                principalTable: "Sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PassNumberSeries_Sites_SiteId",
                table: "PassNumberSeries",
                column: "SiteId",
                principalTable: "Sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_VisitorVisits_Sites_SiteId",
                table: "VisitorVisits",
                column: "SiteId",
                principalTable: "Sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
