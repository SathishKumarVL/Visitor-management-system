-- Additive schema updates for existing databases with live visitor data.
-- Safe to re-run: never drops/renames existing columns; only adds missing ones with nullable/default values.

-- Visitors.Gender (nullable — legacy visitors keep NULL until next registration)
IF COL_LENGTH('dbo.Visitors', 'Gender') IS NULL
  ALTER TABLE dbo.Visitors ADD Gender int NULL;

-- VisitorVisits.MeetingMinutes (nullable)
IF COL_LENGTH('dbo.VisitorVisits', 'MeetingMinutes') IS NULL
  ALTER TABLE dbo.VisitorVisits ADD MeetingMinutes nvarchar(4000) NULL;

-- VisitorVisits.IsManualCheckOut (NOT NULL with default 0 for all existing rows)
IF COL_LENGTH('dbo.VisitorVisits', 'IsManualCheckOut') IS NULL
  ALTER TABLE dbo.VisitorVisits ADD IsManualCheckOut bit NOT NULL
    CONSTRAINT DF_VisitorVisits_IsManualCheckOut DEFAULT ((0));

-- Employees.Code (nullable)
IF COL_LENGTH('dbo.Employees', 'Code') IS NULL
  ALTER TABLE dbo.Employees ADD Code nvarchar(50) NULL;

-- Mark EF history when applying outside Migrate()
IF NOT EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId = N'20260926070506_AddVisitorGender')
  INSERT INTO dbo.__EFMigrationsHistory (MigrationId, ProductVersion)
  VALUES (N'20260926070506_AddVisitorGender', N'9.0.0');

IF NOT EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId = N'20260926074000_AddManualCheckOutFlag')
  INSERT INTO dbo.__EFMigrationsHistory (MigrationId, ProductVersion)
  VALUES (N'20260926074000_AddManualCheckOutFlag', N'9.0.0');
