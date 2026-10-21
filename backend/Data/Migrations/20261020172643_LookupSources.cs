using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tiaano.Vms.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class LookupSources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LookupSources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SearchUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ApiKeyHeader = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ApiKeyEncrypted = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResultsPath = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IdPath = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DisplayTemplate = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    SavedFieldsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MatchPath = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    MatchAgainst = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    BlockOnMismatch = table.Column<bool>(type: "bit", nullable: false),
                    MinChars = table.Column<int>(type: "int", nullable: false),
                    TimeoutSeconds = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LookupSources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LookupSources_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LookupSources_TenantId_Name",
                table: "LookupSources",
                columns: new[] { "TenantId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LookupSources");
        }
    }
}
