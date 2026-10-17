using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tiaano.Vms.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMeetingHalls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "MeetingHallId",
                table: "VisitorVisits",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MeetingHalls",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    SeatingCapacity = table.Column<int>(type: "int", nullable: false),
                    IntercomNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    LocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MeetingHalls", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MeetingHalls_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MeetingHalls_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VisitorVisits_MeetingHallId_Inside",
                table: "VisitorVisits",
                column: "MeetingHallId",
                unique: true,
                filter: "[MeetingHallId] IS NOT NULL AND [Status] = 5");

            migrationBuilder.CreateIndex(
                name: "IX_MeetingHalls_LocationId",
                table: "MeetingHalls",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_MeetingHalls_TenantId_Code",
                table: "MeetingHalls",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MeetingHalls_TenantId_LocationId",
                table: "MeetingHalls",
                columns: new[] { "TenantId", "LocationId" });

            migrationBuilder.AddForeignKey(
                name: "FK_VisitorVisits_MeetingHalls_MeetingHallId",
                table: "VisitorVisits",
                column: "MeetingHallId",
                principalTable: "MeetingHalls",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_VisitorVisits_MeetingHalls_MeetingHallId",
                table: "VisitorVisits");

            migrationBuilder.DropTable(
                name: "MeetingHalls");

            migrationBuilder.DropIndex(
                name: "IX_VisitorVisits_MeetingHallId_Inside",
                table: "VisitorVisits");

            migrationBuilder.DropColumn(
                name: "MeetingHallId",
                table: "VisitorVisits");
        }
    }
}
