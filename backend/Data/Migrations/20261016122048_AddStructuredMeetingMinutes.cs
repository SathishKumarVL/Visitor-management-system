using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tiaano.Vms.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStructuredMeetingMinutes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MomActionItemsJson",
                table: "VisitorVisits",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MomDecisions",
                table: "VisitorVisits",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "MomEmailSentAt",
                table: "VisitorVisits",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "MomNextMeetingDate",
                table: "VisitorVisits",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "MomRecordedAt",
                table: "VisitorVisits",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MomRecordedBy",
                table: "VisitorVisits",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MomActionItemsJson",
                table: "VisitorVisits");

            migrationBuilder.DropColumn(
                name: "MomDecisions",
                table: "VisitorVisits");

            migrationBuilder.DropColumn(
                name: "MomEmailSentAt",
                table: "VisitorVisits");

            migrationBuilder.DropColumn(
                name: "MomNextMeetingDate",
                table: "VisitorVisits");

            migrationBuilder.DropColumn(
                name: "MomRecordedAt",
                table: "VisitorVisits");

            migrationBuilder.DropColumn(
                name: "MomRecordedBy",
                table: "VisitorVisits");
        }
    }
}
