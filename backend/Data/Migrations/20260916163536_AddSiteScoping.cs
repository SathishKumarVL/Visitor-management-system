using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tiaano.Vms.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSiteScoping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "Sites",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Sites",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedBy",
                table: "Sites",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SiteId",
                table: "Locations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_VisitorVisits_TenantId_SiteId_Status",
                table: "VisitorVisits",
                columns: new[] { "TenantId", "SiteId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Locations_SiteId",
                table: "Locations",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_Locations_TenantId_SiteId",
                table: "Locations",
                columns: new[] { "TenantId", "SiteId" });

            migrationBuilder.AddForeignKey(
                name: "FK_Locations_Sites_SiteId",
                table: "Locations",
                column: "SiteId",
                principalTable: "Sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Locations_Sites_SiteId",
                table: "Locations");

            migrationBuilder.DropIndex(
                name: "IX_VisitorVisits_TenantId_SiteId_Status",
                table: "VisitorVisits");

            migrationBuilder.DropIndex(
                name: "IX_Locations_SiteId",
                table: "Locations");

            migrationBuilder.DropIndex(
                name: "IX_Locations_TenantId_SiteId",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "Sites");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Sites");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                table: "Sites");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "Locations");
        }
    }
}
