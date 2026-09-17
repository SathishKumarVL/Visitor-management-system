using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tiaano.Vms.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class DropEntryExitGates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_VisitorVisits_EntryGates_EntryGateId",
                table: "VisitorVisits");

            migrationBuilder.DropForeignKey(
                name: "FK_VisitorVisits_ExitGates_ExitGateId",
                table: "VisitorVisits");

            migrationBuilder.DropTable(
                name: "EntryGates");

            migrationBuilder.DropTable(
                name: "ExitGates");

            migrationBuilder.DropIndex(
                name: "IX_VisitorVisits_EntryGateId",
                table: "VisitorVisits");

            migrationBuilder.DropIndex(
                name: "IX_VisitorVisits_ExitGateId",
                table: "VisitorVisits");

            migrationBuilder.DropColumn(
                name: "EntryGateId",
                table: "VisitorVisits");

            migrationBuilder.DropColumn(
                name: "ExitGateId",
                table: "VisitorVisits");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "EntryGateId",
                table: "VisitorVisits",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ExitGateId",
                table: "VisitorVisits",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EntryGates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EntryGates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EntryGates_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExitGates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExitGates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExitGates_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VisitorVisits_EntryGateId",
                table: "VisitorVisits",
                column: "EntryGateId");

            migrationBuilder.CreateIndex(
                name: "IX_VisitorVisits_ExitGateId",
                table: "VisitorVisits",
                column: "ExitGateId");

            migrationBuilder.CreateIndex(
                name: "IX_EntryGates_TenantId_Name",
                table: "EntryGates",
                columns: new[] { "TenantId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_ExitGates_TenantId_Name",
                table: "ExitGates",
                columns: new[] { "TenantId", "Name" });

            migrationBuilder.AddForeignKey(
                name: "FK_VisitorVisits_EntryGates_EntryGateId",
                table: "VisitorVisits",
                column: "EntryGateId",
                principalTable: "EntryGates",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_VisitorVisits_ExitGates_ExitGateId",
                table: "VisitorVisits",
                column: "ExitGateId",
                principalTable: "ExitGates",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
