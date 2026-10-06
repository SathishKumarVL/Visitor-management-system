using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tiaano.Vms.Api.Data.Migrations;

/// <summary>
/// Additive only: existing visits get MomNotRequired = 0. Safe to re-run.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260929150000_AddMomNotRequired")]
public partial class AddMomNotRequired : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF COL_LENGTH('dbo.VisitorVisits', 'MomNotRequired') IS NULL
                ALTER TABLE [dbo].[VisitorVisits]
                ADD [MomNotRequired] bit NOT NULL
                    CONSTRAINT [DF_VisitorVisits_MomNotRequired] DEFAULT ((0));
            IF COL_LENGTH('dbo.VisitorVisits', 'MomNotRequiredBy') IS NULL
                ALTER TABLE [dbo].[VisitorVisits] ADD [MomNotRequiredBy] nvarchar(256) NULL;
            IF COL_LENGTH('dbo.VisitorVisits', 'MomNotRequiredAt') IS NULL
                ALTER TABLE [dbo].[VisitorVisits] ADD [MomNotRequiredAt] datetime2 NULL;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF COL_LENGTH('dbo.VisitorVisits', 'MomNotRequiredAt') IS NOT NULL
                ALTER TABLE [dbo].[VisitorVisits] DROP COLUMN [MomNotRequiredAt];
            IF COL_LENGTH('dbo.VisitorVisits', 'MomNotRequiredBy') IS NOT NULL
                ALTER TABLE [dbo].[VisitorVisits] DROP COLUMN [MomNotRequiredBy];
            IF COL_LENGTH('dbo.VisitorVisits', 'MomNotRequired') IS NOT NULL
            BEGIN
                IF OBJECT_ID(N'dbo.DF_VisitorVisits_MomNotRequired', N'D') IS NOT NULL
                    ALTER TABLE [dbo].[VisitorVisits] DROP CONSTRAINT [DF_VisitorVisits_MomNotRequired];
                ALTER TABLE [dbo].[VisitorVisits] DROP COLUMN [MomNotRequired];
            END
            """);
    }
}
