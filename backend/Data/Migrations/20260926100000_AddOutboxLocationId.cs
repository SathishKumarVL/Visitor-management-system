using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tiaano.Vms.Api.Data.Migrations;

/// <summary>
/// LocationId on NotificationOutbox (location SMTP resolution). Same as backend/AddOutboxLocationId.sql;
/// idempotent so databases that already ran the script are unaffected.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260926100000_AddOutboxLocationId")]
public partial class AddOutboxLocationId : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF COL_LENGTH('dbo.NotificationOutbox', 'LocationId') IS NULL
              ALTER TABLE dbo.NotificationOutbox ADD LocationId uniqueidentifier NULL;
            """);
        migrationBuilder.Sql("""
            IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_NotificationOutbox_Locations_LocationId')
              ALTER TABLE dbo.NotificationOutbox WITH CHECK
                ADD CONSTRAINT FK_NotificationOutbox_Locations_LocationId
                FOREIGN KEY (LocationId) REFERENCES dbo.Locations (Id);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Non-destructive: leave location column in place.
    }
}
