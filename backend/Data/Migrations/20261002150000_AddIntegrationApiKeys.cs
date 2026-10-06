using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tiaano.Vms.Api.Data.Migrations;

/// <summary>
/// API keys for other applications calling /integration endpoints. New table only; safe to re-run.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261002150000_AddIntegrationApiKeys")]
public partial class AddIntegrationApiKeys : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF OBJECT_ID(N'dbo.IntegrationApiKeys', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.IntegrationApiKeys (
                    Id uniqueidentifier NOT NULL CONSTRAINT PK_IntegrationApiKeys PRIMARY KEY,
                    TenantId uniqueidentifier NOT NULL
                        CONSTRAINT FK_IntegrationApiKeys_Tenants_TenantId REFERENCES dbo.Tenants (Id),
                    Name nvarchar(100) NOT NULL,
                    KeyPrefix nvarchar(16) NOT NULL,
                    KeyHash nvarchar(64) NOT NULL,
                    Scopes nvarchar(200) NOT NULL,
                    IsActive bit NOT NULL,
                    CreatedAt datetime2 NOT NULL,
                    CreatedBy nvarchar(256) NULL,
                    LastUsedAt datetime2 NULL,
                    RevokedAt datetime2 NULL,
                    RevokedBy nvarchar(256) NULL
                );
                CREATE UNIQUE INDEX IX_IntegrationApiKeys_KeyHash ON dbo.IntegrationApiKeys (KeyHash);
                CREATE INDEX IX_IntegrationApiKeys_TenantId ON dbo.IntegrationApiKeys (TenantId);
            END
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF OBJECT_ID(N'dbo.IntegrationApiKeys', N'U') IS NOT NULL
                DROP TABLE dbo.IntegrationApiKeys;
            """);
    }
}
