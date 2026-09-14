using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tiaano.Vms.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMultiTenantFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var tiaanoId = new Guid("11111111-1111-1111-1111-111111111111");

            migrationBuilder.DropIndex(
                name: "IX_VisitPurposes_Name",
                table: "VisitPurposes");

            migrationBuilder.DropIndex(
                name: "IX_VisitorVisits_VisitNumber",
                table: "VisitorVisits");

            migrationBuilder.DropIndex(
                name: "IX_Visitors_VisitorNumber",
                table: "Visitors");

            migrationBuilder.DropIndex(
                name: "IX_SystemSettings_Key",
                table: "SystemSettings");

            migrationBuilder.DropIndex(
                name: "IX_Locations_Name",
                table: "Locations");

            migrationBuilder.DropIndex(
                name: "IX_Departments_Name",
                table: "Departments");

            migrationBuilder.CreateTable(
                name: "Tenants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    LogoPath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PrimaryColor = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SecondaryColor = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    FaviconPath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "Tenants",
                columns: new[] { "Id", "Code", "Name", "LogoPath", "PrimaryColor", "SecondaryColor", "FaviconPath", "IsActive", "CreatedAt", "UpdatedAt" },
                values: new object[]
                {
                    tiaanoId,
                    "TIAANO",
                    "TIAANO",
                    "/branding/tiaano-logo.png",
                    "#0F766E",
                    "#14B8A6",
                    null,
                    true,
                    DateTime.UtcNow,
                    null
                });

            migrationBuilder.CreateTable(
                name: "Sites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Address = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
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

            migrationBuilder.InsertData(
                table: "Sites",
                columns: new[] { "Id", "TenantId", "Name", "Code", "Address", "IsActive", "IsDefault", "CreatedAt" },
                values: new object[]
                {
                    new Guid("22222222-2222-2222-2222-222222222222"),
                    tiaanoId,
                    "Headquarters",
                    "HQ",
                    null,
                    true,
                    true,
                    DateTime.UtcNow
                });

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "VisitPurposes",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: tiaanoId);

            migrationBuilder.AddColumn<Guid>(
                name: "SiteId",
                table: "VisitorVisits",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "VisitorVisits",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: tiaanoId);

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Visitors",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: tiaanoId);

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "SystemSettings",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: tiaanoId);

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Locations",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: tiaanoId);

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Employees",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: tiaanoId);

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Departments",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: tiaanoId);

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "AuditLogs",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SiteId",
                table: "AspNetUsers",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "AspNetUsers",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: tiaanoId);

            migrationBuilder.CreateIndex(
                name: "IX_VisitPurposes_TenantId_Name",
                table: "VisitPurposes",
                columns: new[] { "TenantId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_VisitorVisits_SiteId",
                table: "VisitorVisits",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_VisitorVisits_TenantId_VisitNumber",
                table: "VisitorVisits",
                columns: new[] { "TenantId", "VisitNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Visitors_TenantId_VisitorNumber",
                table: "Visitors",
                columns: new[] { "TenantId", "VisitorNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SystemSettings_TenantId_Key",
                table: "SystemSettings",
                columns: new[] { "TenantId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Locations_TenantId_Name",
                table: "Locations",
                columns: new[] { "TenantId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_Employees_TenantId",
                table: "Employees",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Departments_TenantId_Name",
                table: "Departments",
                columns: new[] { "TenantId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_SiteId",
                table: "AspNetUsers",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_TenantId",
                table: "AspNetUsers",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Sites_TenantId_Code",
                table: "Sites",
                columns: new[] { "TenantId", "Code" });

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_Code",
                table: "Tenants",
                column: "Code",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUsers_Sites_SiteId",
                table: "AspNetUsers",
                column: "SiteId",
                principalTable: "Sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUsers_Tenants_TenantId",
                table: "AspNetUsers",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Departments_Tenants_TenantId",
                table: "Departments",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Employees_Tenants_TenantId",
                table: "Employees",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Locations_Tenants_TenantId",
                table: "Locations",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SystemSettings_Tenants_TenantId",
                table: "SystemSettings",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Visitors_Tenants_TenantId",
                table: "Visitors",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_VisitorVisits_Sites_SiteId",
                table: "VisitorVisits",
                column: "SiteId",
                principalTable: "Sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_VisitorVisits_Tenants_TenantId",
                table: "VisitorVisits",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_VisitPurposes_Tenants_TenantId",
                table: "VisitPurposes",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUsers_Sites_SiteId",
                table: "AspNetUsers");

            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUsers_Tenants_TenantId",
                table: "AspNetUsers");

            migrationBuilder.DropForeignKey(
                name: "FK_Departments_Tenants_TenantId",
                table: "Departments");

            migrationBuilder.DropForeignKey(
                name: "FK_Employees_Tenants_TenantId",
                table: "Employees");

            migrationBuilder.DropForeignKey(
                name: "FK_Locations_Tenants_TenantId",
                table: "Locations");

            migrationBuilder.DropForeignKey(
                name: "FK_SystemSettings_Tenants_TenantId",
                table: "SystemSettings");

            migrationBuilder.DropForeignKey(
                name: "FK_Visitors_Tenants_TenantId",
                table: "Visitors");

            migrationBuilder.DropForeignKey(
                name: "FK_VisitorVisits_Sites_SiteId",
                table: "VisitorVisits");

            migrationBuilder.DropForeignKey(
                name: "FK_VisitorVisits_Tenants_TenantId",
                table: "VisitorVisits");

            migrationBuilder.DropForeignKey(
                name: "FK_VisitPurposes_Tenants_TenantId",
                table: "VisitPurposes");

            migrationBuilder.DropTable(
                name: "Sites");

            migrationBuilder.DropTable(
                name: "Tenants");

            migrationBuilder.DropIndex(
                name: "IX_VisitPurposes_TenantId_Name",
                table: "VisitPurposes");

            migrationBuilder.DropIndex(
                name: "IX_VisitorVisits_SiteId",
                table: "VisitorVisits");

            migrationBuilder.DropIndex(
                name: "IX_VisitorVisits_TenantId_VisitNumber",
                table: "VisitorVisits");

            migrationBuilder.DropIndex(
                name: "IX_Visitors_TenantId_VisitorNumber",
                table: "Visitors");

            migrationBuilder.DropIndex(
                name: "IX_SystemSettings_TenantId_Key",
                table: "SystemSettings");

            migrationBuilder.DropIndex(
                name: "IX_Locations_TenantId_Name",
                table: "Locations");

            migrationBuilder.DropIndex(
                name: "IX_Employees_TenantId",
                table: "Employees");

            migrationBuilder.DropIndex(
                name: "IX_Departments_TenantId_Name",
                table: "Departments");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_SiteId",
                table: "AspNetUsers");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_TenantId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "VisitPurposes");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "VisitorVisits");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "VisitorVisits");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Visitors");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Departments");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "AspNetUsers");

            migrationBuilder.CreateIndex(
                name: "IX_VisitPurposes_Name",
                table: "VisitPurposes",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_VisitorVisits_VisitNumber",
                table: "VisitorVisits",
                column: "VisitNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Visitors_VisitorNumber",
                table: "Visitors",
                column: "VisitorNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SystemSettings_Key",
                table: "SystemSettings",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Locations_Name",
                table: "Locations",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Departments_Name",
                table: "Departments",
                column: "Name");
        }
    }
}
