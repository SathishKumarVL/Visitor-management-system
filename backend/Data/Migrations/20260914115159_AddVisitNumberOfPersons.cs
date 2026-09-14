using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tiaano.Vms.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVisitNumberOfPersons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "NumberOfPersons",
                table: "VisitorVisits",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NumberOfPersons",
                table: "VisitorVisits");
        }
    }
}
