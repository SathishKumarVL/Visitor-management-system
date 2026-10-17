using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tiaano.Vms.Api.Data.Migrations;

/// <summary>
/// Additive only: existing visits get IsGroupEmployee = 0. Safe to re-run.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261016150000_AddGroupEmployeeVisit")]
public partial class AddGroupEmployeeVisit : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF COL_LENGTH('dbo.VisitorVisits', 'IsGroupEmployee') IS NULL
                ALTER TABLE [dbo].[VisitorVisits]
                ADD [IsGroupEmployee] bit NOT NULL
                    CONSTRAINT [DF_VisitorVisits_IsGroupEmployee] DEFAULT ((0));
            IF COL_LENGTH('dbo.VisitorVisits', 'GroupEmployeeUserId') IS NULL
                ALTER TABLE [dbo].[VisitorVisits] ADD [GroupEmployeeUserId] nvarchar(450) NULL;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF COL_LENGTH('dbo.VisitorVisits', 'GroupEmployeeUserId') IS NOT NULL
                ALTER TABLE [dbo].[VisitorVisits] DROP COLUMN [GroupEmployeeUserId];
            IF COL_LENGTH('dbo.VisitorVisits', 'IsGroupEmployee') IS NOT NULL
            BEGIN
                IF OBJECT_ID(N'dbo.DF_VisitorVisits_IsGroupEmployee', N'D') IS NOT NULL
                    ALTER TABLE [dbo].[VisitorVisits] DROP CONSTRAINT [DF_VisitorVisits_IsGroupEmployee];
                ALTER TABLE [dbo].[VisitorVisits] DROP COLUMN [IsGroupEmployee];
            END
            """);
    }
}
