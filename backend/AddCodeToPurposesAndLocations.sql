BEGIN TRANSACTION;
ALTER TABLE [VisitPurposes] ADD [Code] nvarchar(50) NULL;

ALTER TABLE [Locations] ADD [Code] nvarchar(50) NULL;

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260922125737_AddCodeToPurposesAndLocations', N'9.0.4');

COMMIT;
GO

