using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tiaano.Vms.Api.Data.Migrations;

/// <summary>
/// Additive only: the Mr / Ms prefix chosen for an employee in the Employee menu. Existing employees with a
/// recorded sex get the matching prefix. Safe to re-run.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261017090000_AddEmployeeTitle")]
public partial class AddEmployeeTitle : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF COL_LENGTH('dbo.Employees', 'Title') IS NULL
                ALTER TABLE [dbo].[Employees] ADD [Title] nvarchar(10) NULL;
            """);

        migrationBuilder.Sql("""
            UPDATE [dbo].[Employees]
            SET [Title] = CASE [Gender] WHEN 1 THEN N'Mr' WHEN 2 THEN N'Ms' END
            WHERE [Title] IS NULL AND [Gender] IN (1, 2);
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF COL_LENGTH('dbo.Employees', 'Title') IS NOT NULL
                ALTER TABLE [dbo].[Employees] DROP COLUMN [Title];
            """);
    }
}
