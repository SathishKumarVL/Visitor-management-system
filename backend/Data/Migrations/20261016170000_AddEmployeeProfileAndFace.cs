using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tiaano.Vms.Api.Data.Migrations;

/// <summary>
/// Additive only: employee date of birth, sex and enrolment face, plus the employee record a group-employee
/// visit was recognised as. Existing Admins with a saved menu list get the new Employee menu. Safe to re-run.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261016170000_AddEmployeeProfileAndFace")]
public partial class AddEmployeeProfileAndFace : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF COL_LENGTH('dbo.Employees', 'DateOfBirth') IS NULL
                ALTER TABLE [dbo].[Employees] ADD [DateOfBirth] date NULL;
            IF COL_LENGTH('dbo.Employees', 'Gender') IS NULL
                ALTER TABLE [dbo].[Employees] ADD [Gender] int NULL;
            IF COL_LENGTH('dbo.Employees', 'PhotoPath') IS NULL
                ALTER TABLE [dbo].[Employees] ADD [PhotoPath] nvarchar(260) NULL;
            IF COL_LENGTH('dbo.Employees', 'FaceDescriptor') IS NULL
                ALTER TABLE [dbo].[Employees] ADD [FaceDescriptor] varbinary(4096) NULL;
            IF COL_LENGTH('dbo.Employees', 'FaceDimensions') IS NULL
                ALTER TABLE [dbo].[Employees] ADD [FaceDimensions] int NULL;
            IF COL_LENGTH('dbo.Employees', 'FaceModel') IS NULL
                ALTER TABLE [dbo].[Employees] ADD [FaceModel] nvarchar(50) NULL;
            IF COL_LENGTH('dbo.VisitorVisits', 'GroupEmployeeId') IS NULL
                ALTER TABLE [dbo].[VisitorVisits] ADD [GroupEmployeeId] uniqueidentifier NULL;
            """);

        migrationBuilder.Sql("""
            UPDATE u
            SET u.[AllowedMenuKeysJson] = LEFT(u.[AllowedMenuKeysJson], LEN(u.[AllowedMenuKeysJson]) - 1) + N',"employeemaster"]'
            FROM [dbo].[AspNetUsers] u
            WHERE u.[AllowedMenuKeysJson] LIKE N'[[]"%"]'
              AND u.[AllowedMenuKeysJson] NOT LIKE N'%"employeemaster"%'
              AND EXISTS (
                  SELECT 1 FROM [dbo].[AspNetUserRoles] ur
                  JOIN [dbo].[AspNetRoles] r ON r.[Id] = ur.[RoleId]
                  WHERE ur.[UserId] = u.[Id] AND r.[Name] = N'Admin');
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE [dbo].[AspNetUsers]
            SET [AllowedMenuKeysJson] = REPLACE([AllowedMenuKeysJson], N',"employeemaster"', N'')
            WHERE [AllowedMenuKeysJson] LIKE N'%,"employeemaster"%';
            IF COL_LENGTH('dbo.VisitorVisits', 'GroupEmployeeId') IS NOT NULL
                ALTER TABLE [dbo].[VisitorVisits] DROP COLUMN [GroupEmployeeId];
            IF COL_LENGTH('dbo.Employees', 'FaceModel') IS NOT NULL
                ALTER TABLE [dbo].[Employees] DROP COLUMN [FaceModel];
            IF COL_LENGTH('dbo.Employees', 'FaceDimensions') IS NOT NULL
                ALTER TABLE [dbo].[Employees] DROP COLUMN [FaceDimensions];
            IF COL_LENGTH('dbo.Employees', 'FaceDescriptor') IS NOT NULL
                ALTER TABLE [dbo].[Employees] DROP COLUMN [FaceDescriptor];
            IF COL_LENGTH('dbo.Employees', 'PhotoPath') IS NOT NULL
                ALTER TABLE [dbo].[Employees] DROP COLUMN [PhotoPath];
            IF COL_LENGTH('dbo.Employees', 'Gender') IS NOT NULL
                ALTER TABLE [dbo].[Employees] DROP COLUMN [Gender];
            IF COL_LENGTH('dbo.Employees', 'DateOfBirth') IS NOT NULL
                ALTER TABLE [dbo].[Employees] DROP COLUMN [DateOfBirth];
            """);
    }
}
