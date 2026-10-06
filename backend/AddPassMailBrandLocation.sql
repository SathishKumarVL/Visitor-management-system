-- Location scoping for mail templates and pass series.
-- Safe to re-run. Use GO so ALTER COLUMN is visible before filtered indexes.

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF COL_LENGTH('dbo.MailContentTemplates', 'LocationId') IS NULL
  ALTER TABLE dbo.MailContentTemplates ADD LocationId uniqueidentifier NULL;
GO

IF NOT EXISTS (
  SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_MailContentTemplates_Locations_LocationId')
BEGIN
  ALTER TABLE dbo.MailContentTemplates
    ADD CONSTRAINT FK_MailContentTemplates_Locations_LocationId
    FOREIGN KEY (LocationId) REFERENCES dbo.Locations (Id);
END
GO

IF EXISTS (
  SELECT 1 FROM sys.indexes
  WHERE name = N'IX_MailContentTemplates_TenantId_Kind'
    AND object_id = OBJECT_ID(N'dbo.MailContentTemplates'))
  DROP INDEX IX_MailContentTemplates_TenantId_Kind ON dbo.MailContentTemplates;
GO

SET QUOTED_IDENTIFIER ON;
IF NOT EXISTS (
  SELECT 1 FROM sys.indexes
  WHERE name = N'IX_MailContentTemplates_Tenant_Kind_Default'
    AND object_id = OBJECT_ID(N'dbo.MailContentTemplates'))
  CREATE UNIQUE INDEX IX_MailContentTemplates_Tenant_Kind_Default
    ON dbo.MailContentTemplates (TenantId, Kind)
    WHERE LocationId IS NULL;
GO

SET QUOTED_IDENTIFIER ON;
IF NOT EXISTS (
  SELECT 1 FROM sys.indexes
  WHERE name = N'IX_MailContentTemplates_Tenant_Kind_Location'
    AND object_id = OBJECT_ID(N'dbo.MailContentTemplates'))
  CREATE UNIQUE INDEX IX_MailContentTemplates_Tenant_Kind_Location
    ON dbo.MailContentTemplates (TenantId, Kind, LocationId)
    WHERE LocationId IS NOT NULL;
GO

IF NOT EXISTS (
  SELECT 1 FROM sys.indexes
  WHERE name = N'IX_MailContentTemplates_LocationId'
    AND object_id = OBJECT_ID(N'dbo.MailContentTemplates'))
  CREATE INDEX IX_MailContentTemplates_LocationId ON dbo.MailContentTemplates (LocationId);
GO

IF COL_LENGTH('dbo.PassNumberSeries', 'LocationId') IS NULL
  ALTER TABLE dbo.PassNumberSeries ADD LocationId uniqueidentifier NULL;
GO

IF NOT EXISTS (
  SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_PassNumberSeries_Locations_LocationId')
BEGIN
  ALTER TABLE dbo.PassNumberSeries
    ADD CONSTRAINT FK_PassNumberSeries_Locations_LocationId
    FOREIGN KEY (LocationId) REFERENCES dbo.Locations (Id);
END
GO

SET QUOTED_IDENTIFIER ON;
IF NOT EXISTS (
  SELECT 1 FROM sys.indexes
  WHERE name = N'IX_PassNumberSeries_Tenant_Default'
    AND object_id = OBJECT_ID(N'dbo.PassNumberSeries'))
  CREATE UNIQUE INDEX IX_PassNumberSeries_Tenant_Default
    ON dbo.PassNumberSeries (TenantId)
    WHERE LocationId IS NULL;
GO

SET QUOTED_IDENTIFIER ON;
IF NOT EXISTS (
  SELECT 1 FROM sys.indexes
  WHERE name = N'IX_PassNumberSeries_Tenant_Location'
    AND object_id = OBJECT_ID(N'dbo.PassNumberSeries'))
  CREATE UNIQUE INDEX IX_PassNumberSeries_Tenant_Location
    ON dbo.PassNumberSeries (TenantId, LocationId)
    WHERE LocationId IS NOT NULL;
GO

IF NOT EXISTS (
  SELECT 1 FROM sys.indexes
  WHERE name = N'IX_PassNumberSeries_LocationId'
    AND object_id = OBJECT_ID(N'dbo.PassNumberSeries'))
  CREATE INDEX IX_PassNumberSeries_LocationId ON dbo.PassNumberSeries (LocationId);
GO

IF NOT EXISTS (
  SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId = N'20260926120000_AddPassMailBrandLocation')
  INSERT INTO dbo.__EFMigrationsHistory (MigrationId, ProductVersion)
  VALUES (N'20260926120000_AddPassMailBrandLocation', N'9.0.0');
GO
