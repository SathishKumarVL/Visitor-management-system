using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tiaano.Vms.Api.Data.Migrations;

/// <summary>
/// Additive only: existing visit rows get IsManualCheckOut = 0. Safe to re-run.
/// </summary>
public partial class AddManualCheckOutFlag : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF COL_LENGTH('dbo.VisitorVisits', 'IsManualCheckOut') IS NULL
            BEGIN
                ALTER TABLE [dbo].[VisitorVisits]
                ADD [IsManualCheckOut] bit NOT NULL
                    CONSTRAINT [DF_VisitorVisits_IsManualCheckOut] DEFAULT ((0));
            END
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF COL_LENGTH('dbo.VisitorVisits', 'IsManualCheckOut') IS NOT NULL
            BEGIN
                DECLARE @df sysname =
                    (SELECT dc.name FROM sys.default_constraints dc
                     INNER JOIN sys.columns c ON c.default_object_id = dc.object_id
                     WHERE dc.parent_object_id = OBJECT_ID(N'dbo.VisitorVisits')
                       AND c.name = N'IsManualCheckOut');
                IF @df IS NOT NULL
                    EXEC(N'ALTER TABLE [dbo].[VisitorVisits] DROP CONSTRAINT [' + @df + N']');
                ALTER TABLE [dbo].[VisitorVisits] DROP COLUMN [IsManualCheckOut];
            END
            """);
    }
}
