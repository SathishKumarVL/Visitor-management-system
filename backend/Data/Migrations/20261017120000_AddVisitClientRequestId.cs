using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tiaano.Vms.Api.Data.Migrations;

/// <summary>
/// Additive only: the per-form registration key, unique per tenant, so a resubmitted form cannot create a
/// second visit. Existing visits keep a null key. Safe to re-run.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261017120000_AddVisitClientRequestId")]
public partial class AddVisitClientRequestId : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF COL_LENGTH('dbo.VisitorVisits', 'ClientRequestId') IS NULL
                ALTER TABLE [dbo].[VisitorVisits] ADD [ClientRequestId] uniqueidentifier NULL;
            """);

        migrationBuilder.Sql("""
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_VisitorVisits_TenantId_ClientRequestId'
                           AND object_id = OBJECT_ID(N'dbo.VisitorVisits'))
                CREATE UNIQUE INDEX [IX_VisitorVisits_TenantId_ClientRequestId]
                    ON [dbo].[VisitorVisits] ([TenantId], [ClientRequestId])
                    WHERE [ClientRequestId] IS NOT NULL;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_VisitorVisits_TenantId_ClientRequestId'
                       AND object_id = OBJECT_ID(N'dbo.VisitorVisits'))
                DROP INDEX [IX_VisitorVisits_TenantId_ClientRequestId] ON [dbo].[VisitorVisits];
            IF COL_LENGTH('dbo.VisitorVisits', 'ClientRequestId') IS NOT NULL
                ALTER TABLE [dbo].[VisitorVisits] DROP COLUMN [ClientRequestId];
            """);
    }
}
