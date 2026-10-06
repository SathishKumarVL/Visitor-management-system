using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tiaano.Vms.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class RecyclePassNumbersFilteredUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_VisitorPasses_PassCode",
                table: "VisitorPasses");

            migrationBuilder.CreateTable(
                name: "PassNumberHolds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PassNumberHolds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PassNumberHolds_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VisitorPasses_PassCode",
                table: "VisitorPasses",
                column: "PassCode",
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_PassNumberHolds_TenantId_Number",
                table: "PassNumberHolds",
                columns: new[] { "TenantId", "Number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PassNumberHolds");

            migrationBuilder.DropIndex(
                name: "IX_VisitorPasses_PassCode",
                table: "VisitorPasses");

            migrationBuilder.CreateIndex(
                name: "IX_VisitorPasses_PassCode",
                table: "VisitorPasses",
                column: "PassCode",
                unique: true);
        }
    }
}
