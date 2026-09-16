using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tiaano.Vms.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEmergencyRollCallEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmergencyRollCallEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VisitorVisitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RecordedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmergencyRollCallEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmergencyRollCallEvents_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmergencyRollCallEvents_VisitorVisits_VisitorVisitId",
                        column: x => x.VisitorVisitId,
                        principalTable: "VisitorVisits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmergencyRollCallEvents_TenantId_VisitorVisitId_RecordedAt",
                table: "EmergencyRollCallEvents",
                columns: new[] { "TenantId", "VisitorVisitId", "RecordedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_EmergencyRollCallEvents_VisitorVisitId",
                table: "EmergencyRollCallEvents",
                column: "VisitorVisitId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmergencyRollCallEvents");
        }
    }
}
