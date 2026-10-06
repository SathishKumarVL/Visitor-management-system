using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tiaano.Vms.Api.Data.Migrations;

/// <summary>
/// Scopes short-lived pass number holds to their series (prefix + year) so location series do not share one pool.
/// Additive and safe to re-run: existing holds keep SeriesKey = NULL and expire within 15 minutes as before.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261002120000_AddPassNumberHoldSeriesKey")]
public partial class AddPassNumberHoldSeriesKey : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF COL_LENGTH('dbo.PassNumberHolds', 'SeriesKey') IS NULL
              ALTER TABLE dbo.PassNumberHolds ADD SeriesKey nvarchar(40) NULL;
            """);

        migrationBuilder.Sql("""
            IF EXISTS (SELECT 1 FROM sys.indexes
                       WHERE name = N'IX_PassNumberHolds_TenantId_Number' AND object_id = OBJECT_ID(N'dbo.PassNumberHolds'))
              DROP INDEX IX_PassNumberHolds_TenantId_Number ON dbo.PassNumberHolds;

            IF NOT EXISTS (SELECT 1 FROM sys.indexes
                           WHERE name = N'IX_PassNumberHolds_TenantId_SeriesKey_Number' AND object_id = OBJECT_ID(N'dbo.PassNumberHolds'))
              CREATE UNIQUE INDEX IX_PassNumberHolds_TenantId_SeriesKey_Number
                ON dbo.PassNumberHolds (TenantId, SeriesKey, Number) WHERE [SeriesKey] IS NOT NULL;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Non-destructive: leave the column in place.
    }
}
