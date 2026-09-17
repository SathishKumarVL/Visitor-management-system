using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tiaano.Vms.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUserAllowedMenuKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AllowedMenuKeysJson",
                table: "AspNetUsers",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowedMenuKeysJson",
                table: "AspNetUsers");
        }
    }
}
