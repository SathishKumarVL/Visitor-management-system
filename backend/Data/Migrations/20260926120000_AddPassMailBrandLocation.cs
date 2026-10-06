using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tiaano.Vms.Api.Data.Migrations;

/// <summary>
/// LocationId on MailContentTemplates and PassNumberSeries. Mirrors backend/AddPassMailBrandLocation.sql and is
/// idempotent, so databases that already ran the script are unaffected. Additive only: existing rows keep
/// LocationId = NULL (the tenant-wide default). Unique filtered indexes are only created when existing rows
/// already satisfy them, so startup never fails on legacy data.
/// </summary>
public partial class AddPassMailBrandLocation : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF COL_LENGTH('dbo.MailContentTemplates', 'LocationId') IS NULL
              ALTER TABLE dbo.MailContentTemplates ADD LocationId uniqueidentifier NULL;

            IF COL_LENGTH('dbo.PassNumberSeries', 'LocationId') IS NULL
              ALTER TABLE dbo.PassNumberSeries ADD LocationId uniqueidentifier NULL;
            """);

        migrationBuilder.Sql("""
            IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_MailContentTemplates_Locations_LocationId')
              ALTER TABLE dbo.MailContentTemplates WITH CHECK
                ADD CONSTRAINT FK_MailContentTemplates_Locations_LocationId
                FOREIGN KEY (LocationId) REFERENCES dbo.Locations (Id);

            IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_PassNumberSeries_Locations_LocationId')
              ALTER TABLE dbo.PassNumberSeries WITH CHECK
                ADD CONSTRAINT FK_PassNumberSeries_Locations_LocationId
                FOREIGN KEY (LocationId) REFERENCES dbo.Locations (Id);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes
                           WHERE name = N'IX_MailContentTemplates_LocationId' AND object_id = OBJECT_ID(N'dbo.MailContentTemplates'))
              CREATE INDEX IX_MailContentTemplates_LocationId ON dbo.MailContentTemplates (LocationId);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes
                           WHERE name = N'IX_PassNumberSeries_LocationId' AND object_id = OBJECT_ID(N'dbo.PassNumberSeries'))
              CREATE INDEX IX_PassNumberSeries_LocationId ON dbo.PassNumberSeries (LocationId);
            """);

        // The original one-template-per-kind index blocks per-location templates; replace it with filtered ones.
        migrationBuilder.Sql("""
            IF EXISTS (SELECT 1 FROM sys.indexes
                       WHERE name = N'IX_MailContentTemplates_TenantId_Kind' AND object_id = OBJECT_ID(N'dbo.MailContentTemplates'))
              DROP INDEX IX_MailContentTemplates_TenantId_Kind ON dbo.MailContentTemplates;

            IF NOT EXISTS (SELECT 1 FROM sys.indexes
                           WHERE name = N'IX_MailContentTemplates_Tenant_Kind_Default' AND object_id = OBJECT_ID(N'dbo.MailContentTemplates'))
               AND NOT EXISTS (SELECT 1 FROM dbo.MailContentTemplates WHERE LocationId IS NULL
                               GROUP BY TenantId, Kind HAVING COUNT(*) > 1)
              CREATE UNIQUE INDEX IX_MailContentTemplates_Tenant_Kind_Default
                ON dbo.MailContentTemplates (TenantId, Kind) WHERE LocationId IS NULL;

            IF NOT EXISTS (SELECT 1 FROM sys.indexes
                           WHERE name = N'IX_MailContentTemplates_Tenant_Kind_Location' AND object_id = OBJECT_ID(N'dbo.MailContentTemplates'))
               AND NOT EXISTS (SELECT 1 FROM dbo.MailContentTemplates WHERE LocationId IS NOT NULL
                               GROUP BY TenantId, Kind, LocationId HAVING COUNT(*) > 1)
              CREATE UNIQUE INDEX IX_MailContentTemplates_Tenant_Kind_Location
                ON dbo.MailContentTemplates (TenantId, Kind, LocationId) WHERE LocationId IS NOT NULL;

            IF NOT EXISTS (SELECT 1 FROM sys.indexes
                           WHERE name = N'IX_PassNumberSeries_Tenant_Default' AND object_id = OBJECT_ID(N'dbo.PassNumberSeries'))
               AND NOT EXISTS (SELECT 1 FROM dbo.PassNumberSeries WHERE LocationId IS NULL
                               GROUP BY TenantId HAVING COUNT(*) > 1)
              CREATE UNIQUE INDEX IX_PassNumberSeries_Tenant_Default
                ON dbo.PassNumberSeries (TenantId) WHERE LocationId IS NULL;

            IF NOT EXISTS (SELECT 1 FROM sys.indexes
                           WHERE name = N'IX_PassNumberSeries_Tenant_Location' AND object_id = OBJECT_ID(N'dbo.PassNumberSeries'))
               AND NOT EXISTS (SELECT 1 FROM dbo.PassNumberSeries WHERE LocationId IS NOT NULL
                               GROUP BY TenantId, LocationId HAVING COUNT(*) > 1)
              CREATE UNIQUE INDEX IX_PassNumberSeries_Tenant_Location
                ON dbo.PassNumberSeries (TenantId, LocationId) WHERE LocationId IS NOT NULL;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Non-destructive: leave location columns in place.
    }
}
