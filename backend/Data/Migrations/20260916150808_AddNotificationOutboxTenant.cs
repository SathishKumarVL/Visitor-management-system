using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tiaano.Vms.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationOutboxTenant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "NotificationOutbox",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationOutbox_TenantId_IsSent",
                table: "NotificationOutbox",
                columns: new[] { "TenantId", "IsSent" });

            migrationBuilder.AddForeignKey(
                name: "FK_NotificationOutbox_Tenants_TenantId",
                table: "NotificationOutbox",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_NotificationOutbox_Tenants_TenantId",
                table: "NotificationOutbox");

            migrationBuilder.DropIndex(
                name: "IX_NotificationOutbox_TenantId_IsSent",
                table: "NotificationOutbox");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "NotificationOutbox");
        }
    }
}
