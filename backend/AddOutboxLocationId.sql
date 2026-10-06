-- Additive: stamp location on notification outbox for location SMTP resolution.
IF COL_LENGTH('dbo.NotificationOutbox', 'LocationId') IS NULL
  ALTER TABLE dbo.NotificationOutbox ADD LocationId uniqueidentifier NULL;

IF NOT EXISTS (
  SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_NotificationOutbox_Locations_LocationId')
BEGIN
  ALTER TABLE dbo.NotificationOutbox WITH CHECK
    ADD CONSTRAINT FK_NotificationOutbox_Locations_LocationId
    FOREIGN KEY (LocationId) REFERENCES dbo.Locations (Id);
END

IF NOT EXISTS (
  SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId = N'20260926100000_AddOutboxLocationId')
  INSERT INTO dbo.__EFMigrationsHistory (MigrationId, ProductVersion)
  VALUES (N'20260926100000_AddOutboxLocationId', N'9.0.0');
