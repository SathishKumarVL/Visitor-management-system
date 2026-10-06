using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tiaano.Vms.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUserFaceLogins : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserFaceLogins",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    PersonName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Descriptor = table.Column<byte[]>(type: "varbinary(4096)", maxLength: 4096, nullable: false),
                    Dimensions = table.Column<int>(type: "int", nullable: false),
                    Model = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastUsedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserFaceLogins", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserFaceLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserFaceLogins_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MailContentTemplates_TenantId",
                table: "MailContentTemplates",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_UserFaceLogins_Model",
                table: "UserFaceLogins",
                column: "Model");

            migrationBuilder.CreateIndex(
                name: "IX_UserFaceLogins_TenantId",
                table: "UserFaceLogins",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_UserFaceLogins_UserId",
                table: "UserFaceLogins",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserFaceLogins");

            migrationBuilder.DropIndex(
                name: "IX_MailContentTemplates_TenantId",
                table: "MailContentTemplates");
        }
    }
}
