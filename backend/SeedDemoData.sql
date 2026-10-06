/*
  Tiaano VMS - demo data for the client corrections (DEVELOPMENT DATABASE ONLY).

  Every row this script creates is tagged CreatedBy = 'demo-seed', and running it again first removes the
  previous demo rows, so it is safe to re-run. Dates are relative to today (IST). Demo users and the Fibtec
  location are kept on a re-run (so their passwords keep working) and only deleted in 'remove' mode.

  What it creates
    - Location "Fibtec Plant" (L-FIB) next to the existing HQ location, with its own pass series FIB-<year>-.
    - 3 departments and 5-6 employees per location (one deactivated), all with e-mail addresses.
    - Location-bound users: hq.reception, fibtec.reception, fibtec.admin and host priya.raman (created without
      a password; set one with the reset-password command, see the bottom of this file).
    - 28 visitors / 48 visits over the last 2 weeks, today and tomorrow:
        today: visitors inside (with pass numbers, MoM pending/saved), normal and MANUAL check-outs,
        expected and pending-approval visits, and returning visitors whose entry number carries today's date.

  Run:     sqlcmd -S localhost -E -I -d TiaanoVms -i backend\SeedDemoData.sql
  Remove:  change @Mode below to 'remove' and run it again.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;

DECLARE @Mode nvarchar(10) = N'seed';   -- 'seed' = remove old demo rows then insert, 'remove' = remove only

IF DB_NAME() <> N'TiaanoVms'
BEGIN
    RAISERROR('Demo data is only for the development database TiaanoVms. Nothing was changed.', 16, 1);
    RETURN;
END

DECLARE @Tag nvarchar(20) = N'demo-seed';
DECLARE @Tenant uniqueidentifier = (SELECT TOP 1 Id FROM dbo.Tenants ORDER BY CreatedAt);
DECLARE @NowUtc datetime2 = SYSUTCDATETIME();
DECLARE @Today date = CAST(DATEADD(minute, 330, @NowUtc) AS date);
DECLARE @Prefix nvarchar(20) = ISNULL((SELECT TOP 1 NULLIF(LTRIM(RTRIM([Value])), N'') FROM dbo.SystemSettings WHERE [Key] = N'VisitorIdPrefix'), N'VMS');

BEGIN TRANSACTION;

/* ---------------------------------------------------------------- remove previous demo rows */
DECLARE @DemoEmp TABLE (Id uniqueidentifier PRIMARY KEY);
INSERT INTO @DemoEmp SELECT Id FROM dbo.Employees WHERE CreatedBy = @Tag;

DECLARE @DemoVisit TABLE (Id uniqueidentifier PRIMARY KEY);
INSERT INTO @DemoVisit
SELECT Id FROM dbo.VisitorVisits WHERE CreatedBy = @Tag OR HostEmployeeId IN (SELECT Id FROM @DemoEmp);

DECLARE @DemoUser TABLE (Id nvarchar(450) PRIMARY KEY);
INSERT INTO @DemoUser SELECT Id FROM dbo.AspNetUsers WHERE CreatedBy = @Tag;

DELETE a FROM dbo.VisitFeedbackAnswers a JOIN dbo.VisitFeedbacks f ON f.Id = a.VisitFeedbackId
WHERE f.VisitorVisitId IN (SELECT Id FROM @DemoVisit);
DELETE FROM dbo.VisitFeedbacks   WHERE VisitorVisitId IN (SELECT Id FROM @DemoVisit);
DELETE FROM dbo.VisitorDocuments WHERE VisitorVisitId IN (SELECT Id FROM @DemoVisit);
DELETE FROM dbo.VisitorVisits    WHERE Id IN (SELECT Id FROM @DemoVisit);   -- passes, approvals, purposes, locations cascade

DELETE v FROM dbo.Visitors v
WHERE v.CreatedBy = @Tag
  AND NOT EXISTS (SELECT 1 FROM dbo.VisitorVisits x WHERE x.VisitorId = v.Id)
  AND NOT EXISTS (SELECT 1 FROM dbo.VisitFeedbacks f WHERE f.VisitorId = v.Id);

UPDATE dbo.Employees SET UserId = NULL WHERE Id IN (SELECT Id FROM @DemoEmp);

DELETE e FROM dbo.Employees e
WHERE e.Id IN (SELECT Id FROM @DemoEmp)
  AND NOT EXISTS (SELECT 1 FROM dbo.VisitorVisits x WHERE x.HostEmployeeId = e.Id)
  AND NOT EXISTS (SELECT 1 FROM dbo.Approvals a WHERE a.HostEmployeeId = e.Id);

DELETE d FROM dbo.Departments d
WHERE d.CreatedBy = @Tag
  AND NOT EXISTS (SELECT 1 FROM dbo.Employees e WHERE e.DepartmentId = d.Id)
  AND NOT EXISTS (SELECT 1 FROM dbo.VisitorVisits x WHERE x.DepartmentId = d.Id);

IF @Mode = N'remove'
BEGIN
    -- Users and the demo location survive a re-seed so passwords set on them keep working.
    UPDATE dbo.VisitorVisits SET CheckedInByUserId = NULL     WHERE CheckedInByUserId     IN (SELECT Id FROM @DemoUser);
    UPDATE dbo.VisitorVisits SET CheckedOutByUserId = NULL    WHERE CheckedOutByUserId    IN (SELECT Id FROM @DemoUser);
    UPDATE dbo.VisitorVisits SET SecurityCheckInUserId = NULL WHERE SecurityCheckInUserId IN (SELECT Id FROM @DemoUser);
    UPDATE dbo.VisitorPasses SET IssuedByUserId = NULL        WHERE IssuedByUserId        IN (SELECT Id FROM @DemoUser);
    UPDATE dbo.Approvals     SET ActionByUserId = NULL        WHERE ActionByUserId        IN (SELECT Id FROM @DemoUser);
    UPDATE dbo.Approvals     SET RequestedToUserId = NULL     WHERE RequestedToUserId     IN (SELECT Id FROM @DemoUser);
    DELETE FROM dbo.AspNetUsers WHERE Id IN (SELECT Id FROM @DemoUser);      -- roles, tokens, allocations cascade

    DECLARE @OldDemoLoc TABLE (Id uniqueidentifier PRIMARY KEY);
    INSERT INTO @OldDemoLoc SELECT Id FROM dbo.Locations WHERE CreatedBy = @Tag;
    DELETE a FROM dbo.PassNumberAllocations a JOIN dbo.PassNumberSeries s ON s.Id = a.SeriesId
    WHERE s.LocationId IN (SELECT Id FROM @OldDemoLoc);
    DELETE FROM dbo.PassNumberSeries WHERE LocationId IN (SELECT Id FROM @OldDemoLoc);
    UPDATE dbo.NotificationOutbox SET LocationId = NULL WHERE LocationId IN (SELECT Id FROM @OldDemoLoc);
    DELETE l FROM dbo.Locations l
    WHERE l.Id IN (SELECT Id FROM @OldDemoLoc)
      AND NOT EXISTS (SELECT 1 FROM dbo.Departments x WHERE x.LocationId = l.Id)
      AND NOT EXISTS (SELECT 1 FROM dbo.Employees x WHERE x.LocationId = l.Id)
      AND NOT EXISTS (SELECT 1 FROM dbo.VisitorVisitLocations x WHERE x.LocationId = l.Id)
      AND NOT EXISTS (SELECT 1 FROM dbo.VisitPurposes x WHERE x.LocationId = l.Id)
      AND NOT EXISTS (SELECT 1 FROM dbo.FeedbackQuestions x WHERE x.LocationId = l.Id)
      AND NOT EXISTS (SELECT 1 FROM dbo.MailContentTemplates x WHERE x.LocationId = l.Id);

    COMMIT TRANSACTION;
    PRINT 'Demo data removed.';
    RETURN;
END

/* ---------------------------------------------------------------- locations and pass series */
DECLARE @LocHq uniqueidentifier = (SELECT TOP 1 Id FROM dbo.Locations WHERE TenantId = @Tenant AND Code = N'L-HQ');
IF @LocHq IS NULL
    SET @LocHq = (SELECT TOP 1 Id FROM dbo.Locations WHERE TenantId = @Tenant AND IsActive = 1 AND Code <> N'L-FIB' ORDER BY SortOrder, CreatedAt);
IF @LocHq IS NULL
BEGIN
    SET @LocHq = NEWID();
    INSERT INTO dbo.Locations (Id, Name, Code, RequiresPlantNumber, RequiresOtherText, IsActive, SortOrder, CreatedAt, CreatedBy, TenantId)
    VALUES (@LocHq, N'Tiaano Headquarters', N'L-HQ', 0, 0, 1, 1, @NowUtc, @Tag, @Tenant);
END

DECLARE @LocFib uniqueidentifier = (SELECT TOP 1 Id FROM dbo.Locations WHERE TenantId = @Tenant AND Code = N'L-FIB');
IF @LocFib IS NULL
BEGIN
    SET @LocFib = NEWID();
    INSERT INTO dbo.Locations (Id, Name, Code, RequiresPlantNumber, RequiresOtherText, IsActive, SortOrder, CreatedAt, CreatedBy, TenantId)
    VALUES (@LocFib, N'Fibtec Plant', N'L-FIB', 0, 0, 1, 2, @NowUtc, @Tag, @Tenant);
END

IF NOT EXISTS (SELECT 1 FROM dbo.PassNumberSeries WHERE TenantId = @Tenant AND LocationId = @LocFib)
    INSERT INTO dbo.PassNumberSeries (Id, TenantId, Prefix, Year, StartNumber, EndNumber, CurrentNumber, IsActive, CreatedAt, LocationId)
    VALUES (NEWID(), @Tenant, N'FIB-', NULL, 1, 500, 0, 1, @NowUtc, @LocFib);

/* Pass pool per location, chosen the same way the allocator does (location series first, then the default). */
DECLARE @Pool TABLE (LocKey char(3) PRIMARY KEY, SeriesKey nvarchar(40), StartNumber int, EndNumber int);
INSERT INTO @Pool (LocKey, SeriesKey, StartNumber, EndNumber)
SELECT k.LocKey, s.SeriesKey, s.StartNumber, s.EndNumber
FROM (VALUES ('HQ ', @LocHq), ('FIB', @LocFib)) k(LocKey, LocId)
OUTER APPLY (
    SELECT TOP 1
        LEFT(LTRIM(RTRIM(Prefix)), 20) + CAST(ISNULL(Year, YEAR(@Today)) AS nvarchar(4)) + N'-' AS SeriesKey,
        StartNumber, EndNumber
    FROM dbo.PassNumberSeries
    WHERE TenantId = @Tenant AND IsActive = 1 AND StartNumber <= EndNumber
      AND (LocationId = k.LocId OR LocationId IS NULL)
    ORDER BY CASE WHEN LocationId = k.LocId THEN 0 ELSE 1 END, CreatedAt
) s;

/* ---------------------------------------------------------------- departments and employees */
CREATE TABLE #Dept (Code nvarchar(20) PRIMARY KEY, Id uniqueidentifier NOT NULL DEFAULT NEWID(), Name nvarchar(100), LocKey char(3), SortOrder int);
INSERT INTO #Dept (Code, Name, LocKey, SortOrder) VALUES
 (N'HQ-HR',   N'Human Resources',   'HQ ', 11),
 (N'HQ-FIN',  N'Finance',           'HQ ', 12),
 (N'HQ-IT',   N'IT Services',       'HQ ', 13),
 (N'FIB-PRD', N'Production',        'FIB', 21),
 (N'FIB-QA',  N'Quality Assurance', 'FIB', 22),
 (N'FIB-MNT', N'Maintenance',       'FIB', 23);

INSERT INTO dbo.Departments (Id, Name, Code, IsActive, SortOrder, CreatedAt, CreatedBy, TenantId, LocationId)
SELECT Id, Name, Code, 1, SortOrder, @NowUtc, @Tag, @Tenant, CASE LocKey WHEN 'HQ ' THEN @LocHq ELSE @LocFib END
FROM #Dept;

CREATE TABLE #Emp (Code nvarchar(20) PRIMARY KEY, Id uniqueidentifier NOT NULL DEFAULT NEWID(), FullName nvarchar(100),
                   Email nvarchar(256), Phone nvarchar(20), Intercom nvarchar(10), Designation nvarchar(100), DeptCode nvarchar(20), IsActive bit);
INSERT INTO #Emp (Code, FullName, Email, Phone, Intercom, Designation, DeptCode, IsActive) VALUES
 (N'HQ-101',  N'Priya Raman',          N'priya.raman@tiaano.example',      N'9840011101', N'101', N'HR Manager',            N'HQ-HR',   1),
 (N'HQ-102',  N'Arjun Mehta',          N'arjun.mehta@tiaano.example',      N'9840011102', N'102', N'Finance Controller',    N'HQ-FIN',  1),
 (N'HQ-103',  N'Kavitha Subramanian',  N'kavitha.s@tiaano.example',        N'9840011103', N'103', N'IT Lead',               N'HQ-IT',   1),
 (N'HQ-104',  N'Rahul Nair',           N'rahul.nair@tiaano.example',       N'9840011104', N'104', N'Talent Acquisition',    N'HQ-HR',   1),
 (N'HQ-105',  N'Deepa Krishnan',       N'deepa.krishnan@tiaano.example',   N'9840011105', N'105', N'Accounts Executive',    N'HQ-FIN',  1),
 (N'HQ-106',  N'Manoj Pillai',         N'manoj.pillai@tiaano.example',     N'9840011106', N'106', N'System Administrator',  N'HQ-IT',   0),
 (N'FIB-201', N'Suresh Babu',          N'suresh.babu@fibtec.example',      N'9840022201', N'201', N'Production Head',       N'FIB-PRD', 1),
 (N'FIB-202', N'Lakshmi Narayanan',    N'lakshmi.n@fibtec.example',        N'9840022202', N'202', N'QA Manager',            N'FIB-QA',  1),
 (N'FIB-203', N'Vignesh Kumar',        N'vignesh.kumar@fibtec.example',    N'9840022203', N'203', N'Maintenance Engineer',  N'FIB-MNT', 1),
 (N'FIB-204', N'Anitha Joseph',        N'anitha.joseph@fibtec.example',    N'9840022204', N'204', N'Plant Administrator',   N'FIB-PRD', 1),
 (N'FIB-205', N'Karthik Rajan',        N'karthik.rajan@fibtec.example',    N'9840022205', N'205', N'QA Inspector',          N'FIB-QA',  1);

INSERT INTO dbo.Employees (Id, FullName, Email, Phone, Intercom, Designation, DepartmentId, IsActive, CreatedAt, CreatedBy, TenantId, Code, LocationId)
SELECT e.Id, e.FullName, e.Email, e.Phone, e.Intercom, e.Designation, d.Id, e.IsActive, @NowUtc, @Tag, @Tenant, e.Code,
       CASE d.LocKey WHEN 'HQ ' THEN @LocHq ELSE @LocFib END
FROM #Emp e JOIN #Dept d ON d.Code = e.DeptCode;

/* ---------------------------------------------------------------- location-bound users */
CREATE TABLE #Usr (UserName nvarchar(100) PRIMARY KEY, Id nvarchar(450) NOT NULL DEFAULT LOWER(CONVERT(nvarchar(36), NEWID())),
                   FullName nvarchar(100), Email nvarchar(256), RoleName nvarchar(50), LocKey char(3), EmpCode nvarchar(20) NULL);
INSERT INTO #Usr (UserName, FullName, Email, RoleName, LocKey, EmpCode) VALUES
 (N'hq.reception',     N'HQ Reception Desk',     N'hq.reception@tiaano.example',     N'Reception', 'HQ ', NULL),
 (N'fibtec.reception', N'Fibtec Reception Desk', N'fibtec.reception@fibtec.example', N'Reception', 'FIB', NULL),
 (N'fibtec.admin',     N'Fibtec Plant Admin',    N'fibtec.admin@fibtec.example',     N'Admin',     'FIB', NULL),
 (N'priya.raman',      N'Priya Raman',           N'priya.raman@tiaano.example',      N'Host',      'HQ ', N'HQ-101');

-- A real (non-demo) account with the same name is never touched; an earlier demo account is reused.
DELETE u FROM #Usr u
WHERE EXISTS (SELECT 1 FROM dbo.AspNetUsers x WHERE x.NormalizedUserName = UPPER(u.UserName) AND ISNULL(x.CreatedBy, N'') <> @Tag);
UPDATE u SET Id = x.Id FROM #Usr u JOIN dbo.AspNetUsers x ON x.NormalizedUserName = UPPER(u.UserName);

INSERT INTO dbo.AspNetUsers (Id, FullName, DepartmentId, IsActive, MustChangePassword, CreatedAt, CreatedBy, UserName, NormalizedUserName,
                             Email, NormalizedEmail, EmailConfirmed, PasswordHash, SecurityStamp, ConcurrencyStamp, PhoneNumberConfirmed,
                             TwoFactorEnabled, LockoutEnabled, AccessFailedCount, LocationId, TenantId)
SELECT u.Id, u.FullName, NULL, 1, 0, @NowUtc, @Tag, u.UserName, UPPER(u.UserName), u.Email, UPPER(u.Email), 1, NULL,
       UPPER(REPLACE(CONVERT(nvarchar(36), NEWID()), N'-', N'')), CONVERT(nvarchar(36), NEWID()), 0, 0, 1, 0,
       CASE u.LocKey WHEN 'HQ ' THEN @LocHq ELSE @LocFib END, @Tenant
FROM #Usr u
WHERE NOT EXISTS (SELECT 1 FROM dbo.AspNetUsers x WHERE x.Id = u.Id);

UPDATE x SET LocationId = CASE u.LocKey WHEN 'HQ ' THEN @LocHq ELSE @LocFib END, IsActive = 1
FROM dbo.AspNetUsers x JOIN #Usr u ON u.Id = x.Id;

INSERT INTO dbo.AspNetUserRoles (UserId, RoleId)
SELECT u.Id, r.Id FROM #Usr u JOIN dbo.AspNetRoles r ON r.NormalizedName = UPPER(u.RoleName)
WHERE NOT EXISTS (SELECT 1 FROM dbo.AspNetUserRoles x WHERE x.UserId = u.Id AND x.RoleId = r.Id);

UPDATE e SET UserId = u.Id
FROM dbo.Employees e JOIN #Emp te ON te.Id = e.Id JOIN #Usr u ON u.EmpCode = te.Code;

DECLARE @HqDesk nvarchar(450)  = (SELECT Id FROM dbo.AspNetUsers WHERE NormalizedUserName = N'HQ.RECEPTION');
DECLARE @FibDesk nvarchar(450) = (SELECT Id FROM dbo.AspNetUsers WHERE NormalizedUserName = N'FIBTEC.RECEPTION');

/* ---------------------------------------------------------------- visitors */
CREATE TABLE #Vis (VKey char(3) PRIMARY KEY, Id uniqueidentifier NOT NULL DEFAULT NEWID(), FullName nvarchar(100), Company nvarchar(150),
                   Phone nvarchar(20), Email nvarchar(256), Gender int NULL, VisitorNumber nvarchar(50) NULL, CreatedAt datetime2 NULL);
INSERT INTO #Vis (VKey, FullName, Company, Phone, Email) VALUES
 ('V01', N'Ramesh Iyer',      N'Larsen & Toubro',        N'9884010001', N'ramesh.iyer@example.com'),
 ('V02', N'Anjali Sharma',    N'Infosys',                N'9884010002', N'anjali.sharma@example.com'),
 ('V03', N'Mohammed Farooq',  N'Bosch India',            N'9884010003', N'm.farooq@example.com'),
 ('V04', N'Sneha Patil',      N'Deloitte',               N'9884010004', N'sneha.patil@example.com'),
 ('V05', N'Vikram Singh',     N'TVS Motor',              N'9884010005', N'vikram.singh@example.com'),
 ('V06', N'Divya Menon',      N'Zoho Corp',              N'9884010006', N'divya.menon@example.com'),
 ('V07', N'Harish Gupta',     N'Ashok Leyland',          N'9884010007', N'harish.gupta@example.com'),
 ('V08', N'Pooja Reddy',      N'KPMG',                   N'9884010008', N'pooja.reddy@example.com'),
 ('V09', N'Senthil Murugan',  N'Murugan Logistics',      N'9884010009', N'senthil.m@example.com'),
 ('V10', N'Neha Kapoor',      N'Wipro',                  N'9884010010', N'neha.kapoor@example.com'),
 ('V11', N'Ganesh Iyengar',   N'Siemens',                N'9884010011', N'ganesh.iyengar@example.com'),
 ('V12', N'Fatima Begum',     N'HCLTech',                N'9884010012', N'fatima.begum@example.com'),
 ('V13', N'Rajesh Khanna',    N'Tata Steel',             N'9884010013', N'rajesh.khanna@example.com'),
 ('V14', N'Meera Nair',       N'Accenture',              N'9884010014', N'meera.nair@example.com'),
 ('V15', N'Arvind Swamy',     N'Saint-Gobain',           N'9884010015', N'arvind.swamy@example.com'),
 ('V16', N'Kiran Rao',        N'Freshworks',             N'9884010016', N'kiran.rao@example.com'),
 ('V17', N'Balaji Venkat',    N'Venkat Fabricators',     N'9884010017', N'balaji.venkat@example.com'),
 ('V18', N'Shalini Das',      N'ABB India',              N'9884010018', N'shalini.das@example.com'),
 ('V19', N'Prakash Jain',     N'Jain Packaging',         N'9884010019', N'prakash.jain@example.com'),
 ('V20', N'Revathi S',        N'Hyundai Motor India',    N'9884010020', N'revathi.s@example.com'),
 ('V21', N'Naveen Kumar',     N'DTDC Courier',           N'9884010021', N'naveen.kumar@example.com'),
 ('V22', N'Sunita Verma',     N'EY',                     N'9884010022', N'sunita.verma@example.com'),
 ('V23', N'Thomas Mathew',    N'Mathew Electricals',     N'9884010023', N'thomas.mathew@example.com'),
 ('V24', N'Ayesha Khan',      N'Caterpillar',            N'9884010024', N'ayesha.khan@example.com'),
 ('V25', N'Dinesh Karthik',   N'Karthik Engineering',    N'9884010025', N'dinesh.karthik@example.com'),
 ('V26', N'Lavanya Prasad',   N'Renault Nissan',         N'9884010026', N'lavanya.prasad@example.com'),
 ('V27', N'Imran Sheikh',     N'Sheikh Transport',       N'9884010027', N'imran.sheikh@example.com'),
 ('V28', N'Gayathri Rao',     N'Ford India',             N'9884010028', N'gayathri.rao@example.com');

/* ---------------------------------------------------------------- visits
   Status: 0 Expected, 1 PendingApproval, 2 Approved, 3 Rejected, 4 Cancelled, 5 Inside, 6 CheckedOut
   Kind:   W walk-in, E expected, P pre-registered
   Mom:    0 none, 1 minutes saved, 2 marked "not required"
   InAfter = minutes after the visit time the visitor checked in (NULL = never checked in); Dur = minutes inside. */
CREATE TABLE #Spec (Seq int PRIMARY KEY, VKey char(3), EmpCode nvarchar(20), DayOff int, VTime time(0), Status int, Manual bit,
                    InAfter int NULL, Dur int NULL, Kind char(1), Persons int, Mom int, Fb bit);
INSERT INTO #Spec VALUES
 -- today, HQ
 ( 1,'V01',N'HQ-101', 0,'09:30',5,0,   5,NULL,'E',1,0,0),  -- returning visitor (first came 12 days ago), MoM pending
 ( 2,'V02',N'HQ-103', 0,'10:00',5,0,   0,NULL,'E',1,1,0),  -- returning visitor, MoM already saved
 ( 3,'V03',N'HQ-102', 0,'10:45',5,0,   0,NULL,'W',2,0,0),
 ( 4,'V04',N'HQ-104', 0,'08:45',6,0,   0,  75,'E',1,1,1),
 ( 5,'V05',N'HQ-101', 0,'09:15',6,0,  10,  60,'E',1,2,1),
 ( 6,'V06',N'HQ-105', 0,'09:00',6,1,   0, 240,'W',1,1,0),  -- manual check-out today
 ( 7,'V07',N'HQ-102', 0,'10:15',6,1,   5, 180,'E',3,0,0),  -- manual check-out today
 ( 8,'V08',N'HQ-103', 0,'11:00',6,1,   0,  90,'W',1,2,0),  -- manual check-out today
 ( 9,'V09',N'HQ-104', 0,'15:30',2,0,NULL,NULL,'E',1,0,0),  -- expected later today
 (10,'V10',N'HQ-101', 0,'16:30',2,0,NULL,NULL,'E',2,0,0),
 (11,'V11',N'HQ-102', 0,'14:00',1,0,NULL,NULL,'W',1,0,0),  -- waiting for approval
 -- today, Fibtec
 (12,'V17',N'FIB-201',0,'09:00',5,0,   0,NULL,'E',1,0,0),
 (13,'V18',N'FIB-202',0,'10:30',5,0,   5,NULL,'W',1,1,0),
 (14,'V19',N'FIB-203',0,'08:30',6,0,   0,  90,'E',1,1,1),
 (15,'V20',N'FIB-204',0,'09:45',6,1,   0, 200,'W',2,0,0),  -- manual check-out today (Fibtec)
 (16,'V21',N'FIB-201',0,'15:00',2,0,NULL,NULL,'E',1,0,0),
 -- tomorrow (pre-registered)
 (17,'V12',N'HQ-101', 1,'11:00',0,0,NULL,NULL,'P',1,0,0),
 (18,'V22',N'FIB-202',1,'10:00',0,0,NULL,NULL,'P',2,0,0),
 -- last two weeks
 (19,'V13',N'HQ-102', -1,'10:00',6,0,0,  60,'E',1,1,1),
 (20,'V14',N'HQ-103', -1,'11:30',6,1,0, 300,'W',1,0,0),
 (21,'V23',N'FIB-203',-1,'09:30',6,0,0, 120,'E',1,1,1),
 (22,'V11',N'HQ-103', -1,'15:00',6,0,5,  45,'W',1,2,0),
 (23,'V15',N'HQ-101', -2,'14:00',6,0,0,  45,'E',1,1,1),
 (24,'V24',N'FIB-201',-2,'10:00',6,1,0, 240,'W',1,0,0),
 (25,'V07',N'HQ-104', -2,'16:00',6,0,0,  30,'E',1,1,1),
 (26,'V16',N'HQ-105', -3,'10:30',6,0,0,  90,'E',1,1,1),
 (27,'V25',N'FIB-202',-3,'11:00',6,0,0,  60,'E',1,1,1),
 (28,'V09',N'HQ-101', -3,'12:00',4,0,NULL,NULL,'E',1,0,0), -- cancelled
 (29,'V02',N'HQ-103', -4,'15:00',6,0,0,  30,'E',1,1,1),
 (30,'V10',N'HQ-102', -4,'11:00',3,0,NULL,NULL,'W',1,0,0), -- rejected
 (31,'V26',N'FIB-204',-5,'09:15',6,1,0, 180,'W',1,0,0),
 (32,'V03',N'HQ-102', -5,'10:30',6,0,0,  60,'E',1,1,1),
 (33,'V04',N'HQ-104', -6,'10:00',6,0,0,  60,'E',1,1,1),
 (34,'V27',N'FIB-203',-6,'13:00',6,0,0,  30,'W',1,2,0),
 (35,'V18',N'FIB-201',-6,'10:30',6,0,0,  60,'E',1,1,1),
 (36,'V13',N'HQ-101', -7,'11:00',6,0,0,  90,'E',1,1,1),
 (37,'V24',N'FIB-202',-7,'14:00',6,0,0,  60,'E',1,1,0),
 (38,'V28',N'FIB-205',-8,'10:00',6,0,0, 120,'E',1,1,1),
 (39,'V16',N'HQ-101', -8,'15:30',6,0,0,  30,'W',1,2,0),
 (40,'V05',N'HQ-102', -9,'12:00',6,1,0, 360,'W',1,0,0),
 (41,'V19',N'FIB-202',-9,'11:00',6,0,0,  75,'E',1,1,1),
 (42,'V14',N'HQ-103',-10,'09:30',6,0,0,  60,'E',1,1,1),
 (43,'V25',N'FIB-203',-10,'10:30',6,1,0, 200,'W',1,0,0),
 (44,'V06',N'HQ-105',-11,'14:30',6,0,0,  60,'E',1,1,0),
 (45,'V22',N'FIB-205',-11,'11:00',6,0,0,  90,'E',1,1,1),
 (46,'V01',N'HQ-101',-12,'10:00',6,0,0,  45,'E',1,1,1),
 (47,'V12',N'HQ-104',-12,'09:45',6,0,0,  60,'E',1,1,1),
 (48,'V17',N'FIB-201',-13,'10:00',6,0,0, 90,'E',1,1,1);

CREATE TABLE #Visit (
    Seq int PRIMARY KEY, Id uniqueidentifier NOT NULL DEFAULT NEWID(), VKey char(3), VisitorId uniqueidentifier, EmpId uniqueidentifier,
    DeptId uniqueidentifier, LocKey char(3), Intercom nvarchar(10), VisitDate date, VTime time(0), LocalStart datetime2, Status int, Manual bit,
    InAfter int NULL, Dur int NULL, Kind char(1), Persons int, Mom int, Fb bit, DailyNo int, EntryNumber nvarchar(50), VisitNumber nvarchar(50),
    PassNumber nvarchar(50) NULL, CheckInAt datetime2 NULL, CheckOutAt datetime2 NULL, CreatedAt datetime2, IsFirst bit);

INSERT INTO #Visit (Seq, VKey, VisitorId, EmpId, DeptId, LocKey, Intercom, VisitDate, VTime, LocalStart, Status, Manual, InAfter, Dur, Kind, Persons, Mom, Fb)
SELECT s.Seq, s.VKey, v.Id, e.Id, d.Id, d.LocKey, e.Intercom, DATEADD(day, s.DayOff, @Today), s.VTime,
       DATEADD(second, DATEDIFF(second, CAST('00:00' AS time(0)), s.VTime), CAST(DATEADD(day, s.DayOff, @Today) AS datetime2)),
       s.Status, s.Manual, s.InAfter, s.Dur, s.Kind, s.Persons, s.Mom, s.Fb
FROM #Spec s
JOIN #Vis v ON v.VKey = s.VKey
JOIN #Emp e ON e.Code = s.EmpCode
JOIN #Dept d ON d.Code = e.DeptCode;

UPDATE #Visit SET
    CheckInAt  = CASE WHEN InAfter IS NULL THEN NULL ELSE DATEADD(minute, InAfter - 330, LocalStart) END,
    CheckOutAt = CASE WHEN Status = 6 THEN DATEADD(minute, ISNULL(InAfter, 0) + ISNULL(Dur, 60) - 330, LocalStart) END,
    CreatedAt  = CASE Kind WHEN 'W' THEN DATEADD(minute, -340, LocalStart)
                           ELSE DATEADD(day, -1, DATEADD(minute, -330, LocalStart)) END;
UPDATE #Visit SET CreatedAt = DATEADD(minute, -10, @NowUtc) WHERE CreatedAt > @NowUtc;

/* Entry numbers share one daily sequence with visitor numbers ({prefix}-V-yyyyMMdd-NNNN), continuing after existing ones. */
;WITH numbered AS (
    SELECT Seq, VisitDate, ROW_NUMBER() OVER (PARTITION BY VisitDate ORDER BY VTime, Seq) AS rn
    FROM #Visit
)
UPDATE t SET DailyNo = n.rn + ISNULL(mx.MaxNo, 0)
FROM #Visit t
JOIN numbered n ON n.Seq = t.Seq
OUTER APPLY (
    SELECT MAX(TRY_CAST(RIGHT(x.Num, 4) AS int)) AS MaxNo
    FROM (
        SELECT VisitorNumber AS Num FROM dbo.Visitors
        WHERE TenantId = @Tenant AND VisitorNumber LIKE @Prefix + N'-V-' + CONVERT(nchar(8), t.VisitDate, 112) + N'-%'
        UNION ALL
        SELECT EntryNumber FROM dbo.VisitorVisits
        WHERE TenantId = @Tenant AND EntryNumber LIKE @Prefix + N'-V-' + CONVERT(nchar(8), t.VisitDate, 112) + N'-%'
    ) x
) mx;

UPDATE #Visit SET EntryNumber = @Prefix + N'-V-' + CONVERT(nchar(8), VisitDate, 112) + N'-' + RIGHT(N'0000' + CAST(DailyNo AS nvarchar(10)), 4);

;WITH firsts AS (
    SELECT Seq, ROW_NUMBER() OVER (PARTITION BY VKey ORDER BY VisitDate, VTime, Seq) AS rn FROM #Visit
)
UPDATE t SET IsFirst = CASE WHEN f.rn = 1 THEN 1 ELSE 0 END FROM #Visit t JOIN firsts f ON f.Seq = t.Seq;

UPDATE v SET VisitorNumber = t.EntryNumber, CreatedAt = t.CreatedAt
FROM #Vis v JOIN #Visit t ON t.VKey = v.VKey AND t.IsFirst = 1;

/* Visit numbers {prefix}-{year}-NNNNNN continue after the tenant's highest. */
DECLARE @VnPrefix nvarchar(30) = @Prefix + N'-' + CAST(YEAR(@Today) AS nvarchar(4)) + N'-';
DECLARE @VnMax int = ISNULL((
    SELECT MAX(TRY_CAST(SUBSTRING(VisitNumber, LEN(@VnPrefix) + 1, 20) AS int))
    FROM dbo.VisitorVisits WHERE TenantId = @Tenant AND VisitNumber LIKE @VnPrefix + N'%'), 0);
;WITH ordered AS (SELECT Seq, ROW_NUMBER() OVER (ORDER BY VisitDate, VTime, Seq) AS rn FROM #Visit)
UPDATE t SET VisitNumber = @VnPrefix + RIGHT(N'000000' + CAST(@VnMax + o.rn AS nvarchar(10)), 6)
FROM #Visit t JOIN ordered o ON o.Seq = t.Seq;

/* Pass numbers: visits still holding a badge (pending, approved, inside) get the lowest free numbers of their pool. */
;WITH tally AS (
    SELECT TOP (5000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1 AS n
    FROM sys.all_objects a CROSS JOIN sys.all_objects b
),
free AS (
    SELECT p.LocKey, p.SeriesKey, p.StartNumber + t.n AS Num,
           ROW_NUMBER() OVER (PARTITION BY p.LocKey ORDER BY p.StartNumber + t.n) AS rn
    FROM @Pool p
    JOIN tally t ON p.StartNumber + t.n <= p.EndNumber
    WHERE p.SeriesKey IS NOT NULL
      AND NOT EXISTS (
            SELECT 1 FROM dbo.VisitorVisits x
            WHERE x.TenantId = @Tenant AND x.Status IN (1, 2, 5) AND x.PassNumber IS NOT NULL
              AND LEFT(LTRIM(x.PassNumber), LEN(p.SeriesKey)) = p.SeriesKey
              AND TRY_CAST(SUBSTRING(LTRIM(RTRIM(x.PassNumber)), LEN(p.SeriesKey) + 1, 20) AS int) = p.StartNumber + t.n)
      AND NOT EXISTS (
            SELECT 1 FROM dbo.PassNumberHolds h
            WHERE h.TenantId = @Tenant AND h.SeriesKey = p.SeriesKey AND h.Number = p.StartNumber + t.n)
),
holders AS (
    SELECT Seq, LocKey, ROW_NUMBER() OVER (PARTITION BY LocKey ORDER BY Seq) AS rn
    FROM #Visit WHERE Status IN (1, 2, 5)
)
UPDATE v SET PassNumber = f.SeriesKey + RIGHT(N'000000' + CAST(f.Num AS nvarchar(10)), 6)
FROM #Visit v
JOIN holders h ON h.Seq = v.Seq
JOIN free f ON f.LocKey = h.LocKey AND f.rn = h.rn;

/* Finished visits keep the (since released) badge number they wore. */
UPDATE v SET PassNumber = p.SeriesKey + RIGHT(N'000000' + CAST(p.StartNumber + (v.Seq % 7) AS nvarchar(10)), 6)
FROM #Visit v JOIN @Pool p ON p.LocKey = v.LocKey
WHERE v.Status = 6 AND p.SeriesKey IS NOT NULL;

/* ---------------------------------------------------------------- write visitors, visits and children */
INSERT INTO dbo.Visitors (Id, VisitorNumber, FullName, Phone, Email, CompanyName, CreatedAt, CreatedBy, TenantId, Gender)
SELECT Id, VisitorNumber, FullName, Phone, Email, Company, CreatedAt, @Tag, @Tenant, Gender FROM #Vis;

INSERT INTO dbo.VisitorVisits (Id, VisitorId, VisitNumber, VisitorType, Status, VisitDate, VisitTime, ExpectedDate, ExpectedTime,
                               DepartmentId, HostEmployeeId, HostIntercom, PurposeNotes, Notes, IsWalkIn, CheckInAt, CheckedInByUserId,
                               CheckOutAt, CheckedOutByUserId, CreatedAt, CreatedBy, NumberOfPersons, TenantId, PassNumber, MeetingMinutes,
                               IsManualCheckOut, MomNotRequired, MomNotRequiredBy, MomNotRequiredAt, EntryNumber)
SELECT
    t.Id, t.VisitorId, t.VisitNumber,
    CASE t.Kind WHEN 'W' THEN 0 WHEN 'E' THEN 1 ELSE 2 END,
    t.Status, t.VisitDate, t.VTime,
    CASE WHEN t.Kind <> 'W' THEN t.VisitDate END,
    CASE WHEN t.Kind <> 'W' THEN t.VTime END,
    t.DeptId, t.EmpId, t.Intercom,
    CASE t.Seq % 5 WHEN 0 THEN N'Vendor review meeting' WHEN 1 THEN N'Project discussion' WHEN 2 THEN N'Interview'
                   WHEN 3 THEN N'Product demo' ELSE N'Site inspection' END,
    CASE WHEN t.Manual = 1 THEN N'Checked out by reception at end of day (visitor left without checking out).' END,
    CASE WHEN t.Kind = 'W' THEN 1 ELSE 0 END,
    t.CheckInAt,
    CASE WHEN t.CheckInAt IS NOT NULL THEN CASE t.LocKey WHEN 'HQ ' THEN @HqDesk ELSE @FibDesk END END,
    t.CheckOutAt,
    CASE WHEN t.Status = 6 THEN CASE t.LocKey WHEN 'HQ ' THEN @HqDesk ELSE @FibDesk END END,
    t.CreatedAt, @Tag, t.Persons, @Tenant, t.PassNumber,
    CASE WHEN t.Mom = 1 THEN
        CASE t.Seq % 4 WHEN 0 THEN N'Reviewed quotation; revised pricing to be shared by Friday.'
                       WHEN 1 THEN N'Discussed rollout timeline and agreed on a pilot at the plant next month.'
                       WHEN 2 THEN N'Walked through the audit findings; corrective actions assigned to QA.'
                       ELSE N'Candidate interviewed for the role; second round to be scheduled.' END END,
    t.Manual,
    CASE WHEN t.Mom = 2 THEN 1 ELSE 0 END,
    CASE WHEN t.Mom = 2 THEN @Tag END,
    CASE WHEN t.Mom = 2 THEN t.CheckOutAt END,
    t.EntryNumber
FROM #Visit t;

INSERT INTO dbo.VisitorVisitLocations (VisitorVisitId, LocationId)
SELECT Id, CASE LocKey WHEN 'HQ ' THEN @LocHq ELSE @LocFib END FROM #Visit;

DECLARE @Purpose uniqueidentifier = (
    SELECT TOP 1 Id FROM dbo.VisitPurposes
    WHERE TenantId = @Tenant AND IsActive = 1
    ORDER BY CASE WHEN Name = N'Others' THEN 1 ELSE 0 END, SortOrder, CreatedAt);
IF @Purpose IS NOT NULL
    INSERT INTO dbo.VisitorVisitPurposes (VisitorVisitId, VisitPurposeId) SELECT Id, @Purpose FROM #Visit;

INSERT INTO dbo.VisitorPasses (Id, VisitorVisitId, PassCode, IssuedAt, ValidUntil, IsActive, PrintCount, IssuedByUserId, LastPrintedAt)
SELECT NEWID(), t.Id, t.PassNumber,
       ISNULL(t.CheckInAt, t.CreatedAt),
       DATEADD(hour, 12, ISNULL(t.CheckInAt, t.CreatedAt)),
       CASE WHEN t.Status IN (2, 5) THEN 1 ELSE 0 END,
       CASE WHEN t.CheckInAt IS NOT NULL THEN 1 ELSE 0 END,
       CASE t.LocKey WHEN 'HQ ' THEN @HqDesk ELSE @FibDesk END,
       t.CheckInAt
FROM #Visit t
WHERE t.PassNumber IS NOT NULL AND t.Status IN (2, 5, 6);

INSERT INTO dbo.Approvals (Id, VisitorVisitId, Status, HostEmployeeId, Notes, CreatedAt)
SELECT NEWID(), Id, 0, EmpId, N'Walk-in waiting for host approval.', CreatedAt FROM #Visit WHERE Status = 1;
INSERT INTO dbo.Approvals (Id, VisitorVisitId, Status, HostEmployeeId, ActionAt, RejectionReason, CreatedAt)
SELECT NEWID(), Id, 2, EmpId, DATEADD(minute, 15, CreatedAt), N'Host unavailable on this date.', CreatedAt FROM #Visit WHERE Status = 3;

/* Feedback (the visitor's check-out form) with a rating for every active question. */
CREATE TABLE #Fb (VisitId uniqueidentifier PRIMARY KEY, FeedbackId uniqueidentifier NOT NULL DEFAULT NEWID(), Seq int);
INSERT INTO #Fb (VisitId, Seq) SELECT Id, Seq FROM #Visit WHERE Status = 6 AND Fb = 1;

INSERT INTO dbo.VisitFeedbacks (Id, TenantId, VisitorVisitId, VisitorId, VisitorName, CompanyName, Email, Phone, VisitNumber, Comments, SubmittedAt, SubmittedBy)
SELECT f.FeedbackId, @Tenant, t.Id, t.VisitorId, v.FullName, v.Company, v.Email, v.Phone, t.VisitNumber,
       CASE f.Seq % 4 WHEN 0 THEN N'Smooth check-in, thank you.' WHEN 1 THEN N'Reception was very helpful.'
                      WHEN 2 THEN NULL ELSE N'Parking signage could be clearer.' END,
       DATEADD(minute, -2, t.CheckOutAt), @Tag
FROM #Fb f JOIN #Visit t ON t.Id = f.VisitId JOIN #Vis v ON v.VKey = t.VKey;

INSERT INTO dbo.VisitFeedbackAnswers (Id, VisitFeedbackId, FeedbackQuestionId, QuestionText, Rating)
SELECT NEWID(), f.FeedbackId, q.Id, q.Prompt, 3 + ABS(f.Seq + q.SortOrder) % 3
FROM #Fb f CROSS JOIN dbo.FeedbackQuestions q
WHERE q.TenantId = @Tenant AND q.IsActive = 1;

COMMIT TRANSACTION;

/* ---------------------------------------------------------------- summary */
SELECT N'Visits today (all locations)' AS [Check], COUNT(*) AS [Rows] FROM #Visit WHERE VisitDate = @Today
UNION ALL SELECT N'  inside now', COUNT(*) FROM #Visit WHERE VisitDate = @Today AND Status = 5
UNION ALL SELECT N'  checked out today', COUNT(*) FROM #Visit WHERE Status = 6 AND VisitDate = @Today
UNION ALL SELECT N'  manual check-outs today', COUNT(*) FROM #Visit WHERE Status = 6 AND Manual = 1 AND VisitDate = @Today
UNION ALL SELECT N'Demo visits total', COUNT(*) FROM #Visit
UNION ALL SELECT N'Demo employees', COUNT(*) FROM #Emp
UNION ALL SELECT N'Demo users', COUNT(*) FROM #Usr;

SELECT v.FullName AS ReturningVisitor, v.VisitorNumber AS VisitorNumber_FirstVisit, t.EntryNumber AS TodaysEntryNumber, t.PassNumber AS TodaysPass
FROM #Visit t JOIN #Vis v ON v.VKey = t.VKey
WHERE t.VisitDate = @Today AND t.IsFirst = 0;

SELECT UserName, RoleName, CASE LocKey WHEN 'HQ ' THEN N'HQ' ELSE N'Fibtec' END AS Location FROM #Usr;

DROP TABLE #Fb; DROP TABLE #Visit; DROP TABLE #Spec; DROP TABLE #Vis; DROP TABLE #Usr; DROP TABLE #Emp; DROP TABLE #Dept;

/*
  Give the demo users a password (they are created without one):
    cd backend
    $env:ASPNETCORE_ENVIRONMENT='Development'
    dotnet exec bin\Debug\net9.0\Tiaano.Vms.Api.dll reset-password hq.reception
  (repeat for fibtec.reception, fibtec.admin, priya.raman; add --password <pw> to skip the prompt)
*/
