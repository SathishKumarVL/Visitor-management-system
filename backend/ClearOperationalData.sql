/*
  Tiaano VMS - clear operational data before go-live.

  KEEPS:   Settings (SystemSettings), Mail Content, Locations, Departments, users with the SuperAdmin role,
           roles, tenants/licences/modules, migration history, pass-number series (numbers reset).
  DELETES: visitors, visits, photos/documents/face data, passes, approvals, feedback answers,
           employees, all non-SuperAdmin users, notification outbox, refresh tokens, push tokens.
  OPTIONAL (flags below): visit purposes, feedback questions, ID types, integration API keys.

  HOW TO RUN
    1. Take a full backup first, e.g.:
         BACKUP DATABASE [TiaanoVms] TO DISK = N'D:\Backups\TiaanoVms_before_clear.bak' WITH COPY_ONLY, INIT;
    2. Run with @DryRun = 1 (default). It shows the row counts before/after and rolls everything back.
    3. When the counts look right, set @DryRun = 0 and run again.
    4. Stop the site (IIS app pool), then delete the photo files under
         <site>\App_Data\media\tenants\*\visitors\   and   <site>\App_Data\mail-pickup\*.eml
       and start the site again. Everyone except SuperAdmin will need to be re-created and log in again.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;

DECLARE @DryRun              bit = 1;  -- 1 = preview and roll back, 0 = really delete
DECLARE @DeletePurposes      bit = 1;  -- 0 keeps Visit Purposes (an "Others" purpose is recreated automatically)
DECLARE @DeleteFeedbackQs    bit = 1;  -- 0 keeps the feedback questions (defaults are re-seeded when the site starts)
DECLARE @DeleteIdTypes       bit = 0;  -- 1 also deletes ID types (Aadhaar, PAN, ...); usually keep
DECLARE @DeleteApiKeys       bit = 0;  -- 1 also revokes integration API keys used by other applications

IF OBJECT_ID('tempdb..#Counts') IS NOT NULL DROP TABLE #Counts;
CREATE TABLE #Counts (TableName sysname PRIMARY KEY, RowsBefore int NULL, RowsAfter int NULL);
INSERT INTO #Counts (TableName) VALUES
 ('Visitors'),('VisitorVisits'),('VisitorPhotos'),('VisitorDocuments'),('VisitorFaceDescriptors'),
 ('VisitorPasses'),('Approvals'),('VisitFeedbacks'),('VisitFeedbackAnswers'),('EmergencyRollCallEvents'),
 ('Employees'),('AspNetUsers'),('NotificationOutbox'),('RefreshTokens'),('DevicePushTokens'),
 ('PassNumberHolds'),('PassNumberAllocations'),('VisitPurposes'),('FeedbackQuestions'),('IdTypes'),
 ('IntegrationApiKeys'),('SystemSettings'),('MailContentTemplates'),('Locations'),('Departments');

DECLARE @t sysname, @sql nvarchar(400);
DECLARE c CURSOR LOCAL FAST_FORWARD FOR SELECT TableName FROM #Counts;
OPEN c; FETCH NEXT FROM c INTO @t;
WHILE @@FETCH_STATUS = 0
BEGIN
    IF OBJECT_ID(N'dbo.' + @t, N'U') IS NOT NULL
    BEGIN
        SET @sql = N'UPDATE #Counts SET RowsBefore = (SELECT COUNT(*) FROM dbo.' + QUOTENAME(@t) + N') WHERE TableName = @t';
        EXEC sp_executesql @sql, N'@t sysname', @t;
    END
    FETCH NEXT FROM c INTO @t;
END
CLOSE c; DEALLOCATE c;

DECLARE @KeepUsers TABLE (Id nvarchar(450) PRIMARY KEY);
INSERT INTO @KeepUsers (Id)
SELECT DISTINCT ur.UserId
FROM dbo.AspNetUserRoles ur
JOIN dbo.AspNetRoles r ON r.Id = ur.RoleId
WHERE r.NormalizedName = N'SUPERADMIN';

IF NOT EXISTS (SELECT 1 FROM @KeepUsers)
BEGIN
    RAISERROR('No SuperAdmin user found - stopping so nobody is locked out.', 16, 1);
    RETURN;
END

BEGIN TRANSACTION;

    -- Visits and everything hanging off them.
    DELETE FROM dbo.EmergencyRollCallEvents;
    DELETE FROM dbo.VisitFeedbackAnswers;
    DELETE FROM dbo.VisitFeedbacks;
    DELETE FROM dbo.Approvals;
    DELETE FROM dbo.VisitorPasses;
    DELETE FROM dbo.VisitorVisitLocations;
    DELETE FROM dbo.VisitorVisitPurposes;
    DELETE FROM dbo.VisitorPhotos;
    DELETE FROM dbo.VisitorDocuments;
    DELETE FROM dbo.VisitorFaceDescriptors;
    DELETE FROM dbo.VisitorVisits;
    DELETE FROM dbo.Visitors;

    -- Queues, sessions and number reservations.
    DELETE FROM dbo.PassNumberHolds;
    DELETE FROM dbo.NotificationOutbox;
    DELETE FROM dbo.RefreshTokens;
    DELETE FROM dbo.DevicePushTokens;

    -- Employees (people to meet). Their logins are removed with the other non-SuperAdmin users below.
    DELETE FROM dbo.Employees;

    IF @DeletePurposes = 1   DELETE FROM dbo.VisitPurposes;
    IF @DeleteFeedbackQs = 1 DELETE FROM dbo.FeedbackQuestions;
    IF @DeleteIdTypes = 1    DELETE FROM dbo.IdTypes;
    IF @DeleteApiKeys = 1    DELETE FROM dbo.IntegrationApiKeys;

    -- Users: keep SuperAdmin only. Roles, claims, tokens and pass allocations cascade.
    DELETE FROM dbo.PassNumberAllocations WHERE UserId NOT IN (SELECT Id FROM @KeepUsers);
    DELETE FROM dbo.AspNetUserRoles  WHERE UserId NOT IN (SELECT Id FROM @KeepUsers);
    DELETE FROM dbo.AspNetUserClaims WHERE UserId NOT IN (SELECT Id FROM @KeepUsers);
    DELETE FROM dbo.AspNetUserLogins WHERE UserId NOT IN (SELECT Id FROM @KeepUsers);
    DELETE FROM dbo.AspNetUserTokens WHERE UserId NOT IN (SELECT Id FROM @KeepUsers);
    DELETE FROM dbo.AspNetUsers      WHERE Id     NOT IN (SELECT Id FROM @KeepUsers);
    UPDATE dbo.AspNetUsers SET AccessFailedCount = 0, LockoutEnd = NULL;

    -- Start pass numbers again from the beginning of each series / allocation.
    UPDATE dbo.PassNumberSeries      SET CurrentNumber = StartNumber - 1, UpdatedAt = SYSUTCDATETIME();
    UPDATE dbo.PassNumberAllocations SET CurrentNumber = StartNumber - 1, UpdatedAt = SYSUTCDATETIME();

    DECLARE c2 CURSOR LOCAL FAST_FORWARD FOR SELECT TableName FROM #Counts;
    OPEN c2; FETCH NEXT FROM c2 INTO @t;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        IF OBJECT_ID(N'dbo.' + @t, N'U') IS NOT NULL
        BEGIN
            SET @sql = N'UPDATE #Counts SET RowsAfter = (SELECT COUNT(*) FROM dbo.' + QUOTENAME(@t) + N') WHERE TableName = @t';
            EXEC sp_executesql @sql, N'@t sysname', @t;
        END
        FETCH NEXT FROM c2 INTO @t;
    END
    CLOSE c2; DEALLOCATE c2;

    SELECT TableName, RowsBefore, RowsAfter FROM #Counts ORDER BY TableName;
    SELECT u.UserName AS KeptUser, u.Email FROM dbo.AspNetUsers u;

IF @DryRun = 1
BEGIN
    ROLLBACK TRANSACTION;
    PRINT 'DRY RUN: nothing was deleted. Set @DryRun = 0 to apply.';
END
ELSE
BEGIN
    COMMIT TRANSACTION;
    PRINT 'Operational data cleared.';
END

DROP TABLE #Counts;
