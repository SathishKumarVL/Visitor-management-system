using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tiaano.Vms.Api.Data.Migrations;

/// <summary>
/// Additive only, safe to re-run:
/// VisitorVisits.EntryNumber (visitor number for the day of this visit; existing visits stay NULL and keep
/// showing the visitor's original number) and NotificationOutbox sender overrides for host-sent mails.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261003090000_AddVisitEntryNumberAndMailSender")]
public partial class AddVisitEntryNumberAndMailSender : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF COL_LENGTH('dbo.VisitorVisits', 'EntryNumber') IS NULL
                ALTER TABLE [dbo].[VisitorVisits] ADD [EntryNumber] nvarchar(50) NULL;
            IF COL_LENGTH('dbo.NotificationOutbox', 'FromAddress') IS NULL
                ALTER TABLE [dbo].[NotificationOutbox] ADD [FromAddress] nvarchar(256) NULL;
            IF COL_LENGTH('dbo.NotificationOutbox', 'FromName') IS NULL
                ALTER TABLE [dbo].[NotificationOutbox] ADD [FromName] nvarchar(200) NULL;
            IF COL_LENGTH('dbo.NotificationOutbox', 'ReplyTo') IS NULL
                ALTER TABLE [dbo].[NotificationOutbox] ADD [ReplyTo] nvarchar(256) NULL;
            """);
        migrationBuilder.Sql("""
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_VisitorVisits_TenantId_EntryNumber'
                           AND object_id = OBJECT_ID(N'dbo.VisitorVisits'))
                CREATE INDEX [IX_VisitorVisits_TenantId_EntryNumber] ON [dbo].[VisitorVisits] ([TenantId], [EntryNumber]);
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_VisitorVisits_TenantId_EntryNumber'
                       AND object_id = OBJECT_ID(N'dbo.VisitorVisits'))
                DROP INDEX [IX_VisitorVisits_TenantId_EntryNumber] ON [dbo].[VisitorVisits];
            IF COL_LENGTH('dbo.VisitorVisits', 'EntryNumber') IS NOT NULL
                ALTER TABLE [dbo].[VisitorVisits] DROP COLUMN [EntryNumber];
            IF COL_LENGTH('dbo.NotificationOutbox', 'ReplyTo') IS NOT NULL
                ALTER TABLE [dbo].[NotificationOutbox] DROP COLUMN [ReplyTo];
            IF COL_LENGTH('dbo.NotificationOutbox', 'FromName') IS NOT NULL
                ALTER TABLE [dbo].[NotificationOutbox] DROP COLUMN [FromName];
            IF COL_LENGTH('dbo.NotificationOutbox', 'FromAddress') IS NOT NULL
                ALTER TABLE [dbo].[NotificationOutbox] DROP COLUMN [FromAddress];
            """);
    }
}
