using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tiaano.Vms.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class VisitArrivalQr : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ArrivalCodeHash",
                table: "VisitorVisits",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QrText",
                table: "NotificationOutbox",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_VisitorVisits_ArrivalCodeHash",
                table: "VisitorVisits",
                column: "ArrivalCodeHash",
                unique: true,
                filter: "[ArrivalCodeHash] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_VisitorVisits_ArrivalCodeHash",
                table: "VisitorVisits");

            migrationBuilder.DropColumn(
                name: "ArrivalCodeHash",
                table: "VisitorVisits");

            migrationBuilder.DropColumn(
                name: "QrText",
                table: "NotificationOutbox");
        }
    }
}
