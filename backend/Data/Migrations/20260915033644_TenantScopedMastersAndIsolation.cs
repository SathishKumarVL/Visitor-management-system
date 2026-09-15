using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tiaano.Vms.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class TenantScopedMastersAndIsolation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "IdTypes",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "ExitGates",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "EntryGates",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.Sql("""
                UPDATE IdTypes SET TenantId = '11111111-1111-1111-1111-111111111111' WHERE TenantId = '00000000-0000-0000-0000-000000000000';
                UPDATE EntryGates SET TenantId = '11111111-1111-1111-1111-111111111111' WHERE TenantId = '00000000-0000-0000-0000-000000000000';
                UPDATE ExitGates SET TenantId = '11111111-1111-1111-1111-111111111111' WHERE TenantId = '00000000-0000-0000-0000-000000000000';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_IdTypes_TenantId_Name",
                table: "IdTypes",
                columns: new[] { "TenantId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_ExitGates_TenantId_Name",
                table: "ExitGates",
                columns: new[] { "TenantId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_EntryGates_TenantId_Name",
                table: "EntryGates",
                columns: new[] { "TenantId", "Name" });

            migrationBuilder.AddForeignKey(
                name: "FK_EntryGates_Tenants_TenantId",
                table: "EntryGates",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ExitGates_Tenants_TenantId",
                table: "ExitGates",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_IdTypes_Tenants_TenantId",
                table: "IdTypes",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EntryGates_Tenants_TenantId",
                table: "EntryGates");

            migrationBuilder.DropForeignKey(
                name: "FK_ExitGates_Tenants_TenantId",
                table: "ExitGates");

            migrationBuilder.DropForeignKey(
                name: "FK_IdTypes_Tenants_TenantId",
                table: "IdTypes");

            migrationBuilder.DropIndex(
                name: "IX_IdTypes_TenantId_Name",
                table: "IdTypes");

            migrationBuilder.DropIndex(
                name: "IX_ExitGates_TenantId_Name",
                table: "ExitGates");

            migrationBuilder.DropIndex(
                name: "IX_EntryGates_TenantId_Name",
                table: "EntryGates");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "IdTypes");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ExitGates");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "EntryGates");
        }
    }
}
