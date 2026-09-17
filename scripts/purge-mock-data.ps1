<#
.SYNOPSIS
    Removes test residue and sample seed data from the local development database.

.DESCRIPTION
    The integration tests used to write into TiaanoVms, which left hundreds of visitors, audit
    rows and throwaway accounts in the working database. This script restores a clean slate:
    operational data (visitors, visits, photos, faces, audit, notifications) is wiped; sample
    departments, purposes, locations, ID types, gates and fictional employees are removed; and
    the leftover demo logins (reception, security, host) plus any isolation-test tenants go too.

    superadmin and admin are kept so you can still sign in. System settings and the default
    TIAANO tenant / Headquarters site are kept because they are configuration, not sample content.
    The "Others" visit purpose is kept because registration falls back to it.

    After this runs, create your own departments, hosts, purposes, locations and desk accounts
    under Admin before registering visitors.
#>
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [string]$Server = 'localhost',
    [string]$Database = 'TiaanoVms',
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

$sql = @'
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;

BEGIN TRAN;

-- Operational data: everything a visit ever touches.
DELETE FROM VisitorVisitLocations;
DELETE FROM VisitorVisitPurposes;
DELETE FROM Approvals;
DELETE FROM VisitorPasses;
DELETE FROM VisitorPhotos;
DELETE FROM VisitorDocuments;
DELETE FROM VisitorFaceDescriptors;
DELETE FROM VisitorVisits;
DELETE FROM Visitors;
DELETE FROM NotificationOutbox;
DELETE FROM AuditLogs;
DELETE FROM EmergencyRollCallEvents;
DELETE FROM RefreshTokens;

-- Detach every user so optional FKs cannot block the master-data wipe below.
-- Accounts we keep stay; their department and site membership are cleared and can be reassigned.
UPDATE AspNetUsers SET DepartmentId = NULL, SiteId = NULL;

-- Sample master data that used to be seeded. Keep "Others" — registration falls back to it.
DELETE FROM Locations;
DELETE FROM IdTypes;
DELETE FROM EntryGates;
DELETE FROM ExitGates;
DELETE FROM Employees;
DELETE FROM Departments;
DELETE FROM VisitPurposes
WHERE Name <> N'Others'
   OR TenantId NOT IN (SELECT Id FROM Tenants WHERE Code = N'TIAANO');

-- Drop leftover demo and test logins, then the isolation-test tenant and its sites.
DECLARE @KeepUsers TABLE (UserName nvarchar(256));
INSERT INTO @KeepUsers (UserName) VALUES (N'superadmin'), (N'admin');

DELETE ur
FROM AspNetUserRoles ur
INNER JOIN AspNetUsers u ON u.Id = ur.UserId
WHERE u.UserName NOT IN (SELECT UserName FROM @KeepUsers);

DELETE uc
FROM AspNetUserClaims uc
INNER JOIN AspNetUsers u ON u.Id = uc.UserId
WHERE u.UserName NOT IN (SELECT UserName FROM @KeepUsers);

DELETE ul
FROM AspNetUserLogins ul
INNER JOIN AspNetUsers u ON u.Id = ul.UserId
WHERE u.UserName NOT IN (SELECT UserName FROM @KeepUsers);

DELETE ut
FROM AspNetUserTokens ut
INNER JOIN AspNetUsers u ON u.Id = ut.UserId
WHERE u.UserName NOT IN (SELECT UserName FROM @KeepUsers);

DELETE FROM AspNetUsers
WHERE UserName NOT IN (SELECT UserName FROM @KeepUsers);

-- Isolation-test tenants and throwaway sites. Keep only the default TIAANO HQ site.
DELETE FROM Sites
WHERE TenantId NOT IN (SELECT Id FROM Tenants WHERE Code = N'TIAANO')
   OR (Code <> N'HQ' AND IsDefault = 0);

DELETE FROM TenantModuleEntitlements
WHERE TenantId NOT IN (SELECT Id FROM Tenants WHERE Code = N'TIAANO');

DELETE FROM TenantLicenses
WHERE TenantId NOT IN (SELECT Id FROM Tenants WHERE Code = N'TIAANO');

DELETE FROM SystemSettings
WHERE TenantId NOT IN (SELECT Id FROM Tenants WHERE Code = N'TIAANO');

DELETE FROM Tenants WHERE Code <> N'TIAANO';

-- Stale release note left by an overnight hardening run; never read by the application.
DELETE FROM ApplicationReleases;

COMMIT;

SELECT 'Visitors' AS [Table], COUNT(*) AS [Rows] FROM Visitors
UNION ALL SELECT 'VisitorVisits', COUNT(*) FROM VisitorVisits
UNION ALL SELECT 'Employees', COUNT(*) FROM Employees
UNION ALL SELECT 'Departments', COUNT(*) FROM Departments
UNION ALL SELECT 'Locations', COUNT(*) FROM Locations
UNION ALL SELECT 'VisitPurposes', COUNT(*) FROM VisitPurposes
UNION ALL SELECT 'AspNetUsers', COUNT(*) FROM AspNetUsers
UNION ALL SELECT 'Tenants', COUNT(*) FROM Tenants
UNION ALL SELECT 'Sites', COUNT(*) FROM Sites
UNION ALL SELECT 'AuditLogs', COUNT(*) FROM AuditLogs
ORDER BY [Table];
'@

Write-Host "Target: $Server / $Database"
Write-Host "This permanently deletes visitors, visits, sample masters, leftover demo accounts and test tenants."

if (-not $Force -and -not $PSCmdlet.ShouldProcess("$Server/$Database", 'Purge mock and test data')) {
    Write-Host 'Cancelled. Pass -Force to run non-interactively.'
    exit 1
}

$sqlFile = Join-Path $env:TEMP ("vms-purge-{0}.sql" -f [Guid]::NewGuid().ToString('N'))
try {
    [System.IO.File]::WriteAllText($sqlFile, $sql, (New-Object System.Text.UTF8Encoding($false)))
    $output = sqlcmd -S $Server -d $Database -E -C -b -i $sqlFile -W 2>&1
    $output | ForEach-Object { Write-Host $_ }
    if ($LASTEXITCODE -ne 0) { throw "sqlcmd failed with exit code $LASTEXITCODE." }
    if ($output -match 'Msg \d+, Level') { throw "sqlcmd reported an error. The transaction was not committed." }
}
finally {
    Remove-Item $sqlFile -Force -ErrorAction SilentlyContinue
}

Write-Host ''
Write-Host 'Database purged. Sign in as superadmin or admin and create your own masters under Admin.'

$mediaRoot = Join-Path $PSScriptRoot '..\backend\App_Data\media'
if (Test-Path $mediaRoot) {
    Get-ChildItem $mediaRoot -Recurse -Directory -Filter 'visitors' | ForEach-Object {
        Get-ChildItem $_.FullName -File -ErrorAction SilentlyContinue | Remove-Item -Force
        Write-Host "Cleared visitor media under $($_.FullName)"
    }
}
