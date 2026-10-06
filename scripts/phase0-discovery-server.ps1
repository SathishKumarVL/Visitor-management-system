#Requires -RunAsAdministrator
<#
.SYNOPSIS
  TIAANO VMS — Phase 0 read-only production discovery (Windows Server / IIS).

.DESCRIPTION
  Inspects IIS, .NET Hosting Bundle/ANCM, SQL visibility, and /api folder layout.
  Writes C:\TiaanoDeployment\DISCOVERY_REPORT.txt with secrets REDACTED.
  Makes NO configuration changes.

.NOTES
  Run in elevated PowerShell ON the production server, not on a developer PC.
  Do not paste Jwt keys, SQL passwords, or Data Protection keys into chat.
#>

$ErrorActionPreference = 'Continue'
$OutDir = 'C:\TiaanoDeployment'
$Report = Join-Path $OutDir 'DISCOVERY_REPORT.txt'
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

function Redact-Text([string]$text) {
    if ([string]::IsNullOrEmpty($text)) { return $text }
    $t = $text
    $t = [regex]::Replace($t, '(?i)(Password|Pwd)\s*=\s*[^;"]+', '$1=***REDACTED***')
    $t = [regex]::Replace($t, '(?i)(Jwt__Key|Jwt:Key|DataProtectionKey|Seed__DefaultPassword|Seed:DefaultPassword)\s*[=:]\s*\S+', '$1=***REDACTED***')
    $t = [regex]::Replace($t, '(?i)(ConnectionStrings__DefaultConnection|DefaultConnection)\s*[=:]\s*.+', '$1=***REDACTED***')
    $t = [regex]::Replace($t, '(?i)value="[^"]*(Password|Key|Secret|ConnectionString)[^"]*"', 'value="***REDACTED***"')
    return $t
}

function Section([string]$title) {
    "`r`n==== $title ====$((Get-Date).ToString('u'))"
}

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine("TIAANO VMS DISCOVERY REPORT")
[void]$sb.AppendLine("Generated: $(Get-Date -Format o)")
[void]$sb.AppendLine("Host: $env:COMPUTERNAME")
[void]$sb.AppendLine("User: $env:USERNAME")
[void]$sb.AppendLine("NOTE: Secrets redacted. Do not paste real secrets into chat/git.")

# A–B OS / IIS
[void]$sb.AppendLine((Section 'A-B Windows / IIS version'))
try {
    $os = Get-CimInstance Win32_OperatingSystem
    [void]$sb.AppendLine("OS: $($os.Caption) | Version: $($os.Version) | Build: $($os.BuildNumber)")
} catch { [void]$sb.AppendLine("OS query failed: $($_.Exception.Message)") }

$iisPresent = $null -ne (Get-Service W3SVC -ErrorAction SilentlyContinue)
[void]$sb.AppendLine("W3SVC present: $iisPresent")
if ($iisPresent) {
    try {
        Import-Module WebAdministration -ErrorAction Stop
        $major = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\InetStp' -ErrorAction SilentlyContinue).MajorVersion
        $minor = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\InetStp' -ErrorAction SilentlyContinue).MinorVersion
        [void]$sb.AppendLine("IIS version: $major.$minor")
    } catch { [void]$sb.AppendLine("IIS module/version: $($_.Exception.Message)") }
}

# C–J Sites / bindings / pools
[void]$sb.AppendLine((Section 'C-J IIS sites, bindings, pools'))
$siteMatch = $null
$apiApp = $null
$poolName = $null
$sitePath = $null

if ($iisPresent) {
    try {
        Import-Module WebAdministration -ErrorAction Stop
        Get-Website | ForEach-Object {
            [void]$sb.AppendLine("SITE: $($_.Name) | State=$($_.State) | PhysicalPath=$($_.physicalPath) | AppPool=$($_.applicationPool)")
        }

        $sites = Get-Website
        foreach ($s in $sites) {
            $bindings = Get-WebBinding -Name $s.Name
            foreach ($b in $bindings) {
                [void]$sb.AppendLine("BINDING site=$($s.Name) | $($b.protocol) | $($b.bindingInformation) | certHash=$($b.certificateHash)")
                if ($b.bindingInformation -match 'visitors\.tiaanosoft\.com') {
                    $siteMatch = $s
                }
            }
        }

        if (-not $siteMatch) {
            # fallback: path or name heuristics
            $siteMatch = $sites | Where-Object { $_.Name -match 'visitor|tiaano' } | Select-Object -First 1
        }

        if ($siteMatch) {
            $sitePath = $siteMatch.physicalPath
            $poolName = $siteMatch.applicationPool
            [void]$sb.AppendLine("MATCHED_SITE: $($siteMatch.Name)")
            [void]$sb.AppendLine("MATCHED_PHYSICAL_PATH: $sitePath")
            [void]$sb.AppendLine("MATCHED_APP_POOL: $poolName")

            $apps = Get-WebApplication -Site $siteMatch.Name
            foreach ($a in $apps) {
                [void]$sb.AppendLine("APP: path=$($a.path) | PhysicalPath=$($a.PhysicalPath) | AppPool=$($a.applicationPool)")
                if ($a.path -eq '/api') { $apiApp = $a }
            }
            [void]$sb.AppendLine("API_IS_IIS_APPLICATION: $($null -ne $apiApp)")
            if ($apiApp) {
                [void]$sb.AppendLine("API_PHYSICAL_PATH: $($apiApp.PhysicalPath)")
                [void]$sb.AppendLine("API_APP_POOL: $($apiApp.applicationPool)")
                $poolName = $apiApp.applicationPool
            }
        } else {
            [void]$sb.AppendLine("No site matched visitors.tiaanosoft.com by binding/name heuristic.")
        }

        Get-ChildItem IIS:\AppPools | ForEach-Object {
            $p = $_
            $clr = (Get-ItemProperty "IIS:\AppPools\$($p.Name)").managedRuntimeVersion
            $mode = (Get-ItemProperty "IIS:\AppPools\$($p.Name)").managedPipelineMode
            $bit = (Get-ItemProperty "IIS:\AppPools\$($p.Name)").enable32BitAppOnWin64
            [void]$sb.AppendLine("POOL: $($p.Name) | State=$($p.State) | CLR='$clr' | Pipeline=$mode | 32bit=$bit")
        }

        if ($poolName) {
            $clr = (Get-ItemProperty "IIS:\AppPools\$poolName").managedRuntimeVersion
            $mode = (Get-ItemProperty "IIS:\AppPools\$poolName").managedPipelineMode
            $bit = (Get-ItemProperty "IIS:\AppPools\$poolName").enable32BitAppOnWin64
            [void]$sb.AppendLine("RELEVANT_POOL=$poolName | CLR='$clr' (empty/No Managed Code expected for ASP.NET Core) | Pipeline=$mode | 32bit=$bit")
        }
    } catch {
        [void]$sb.AppendLine("IIS discovery error: $($_.Exception.Message)")
    }
}

# M–P .NET / ANCM
[void]$sb.AppendLine((Section 'M-P .NET SDKs / runtimes / Hosting Bundle / ANCM'))
try {
    $sdk = & dotnet --list-sdks 2>$null
    [void]$sb.AppendLine("SDKs:`n$sdk")
} catch { [void]$sb.AppendLine("dotnet SDK: not available or failed") }
try {
    $rt = & dotnet --list-runtimes 2>$null
    [void]$sb.AppendLine("Runtimes:`n$rt")
} catch { [void]$sb.AppendLine("dotnet runtimes: failed") }

$ancm = @(
    'HKLM:\SOFTWARE\Microsoft\AspNetCore\Shared Framework',
    'HKLM:\SOFTWARE\WOW6432Node\Microsoft\IIS Extensions\IIS AspNetCore Module V2',
    'HKLM:\SOFTWARE\Microsoft\IIS Extensions\IIS AspNetCore Module V2'
)
foreach ($k in $ancm) {
    [void]$sb.AppendLine("REG $k exists: $(Test-Path $k)")
}
$ancmDll = "${env:ProgramFiles}\IIS\Asp.Net Core Module\V2\aspnetcorev2.dll"
[void]$sb.AppendLine("aspnetcorev2.dll exists: $(Test-Path $ancmDll) path=$ancmDll")
try {
    $mods = Get-WebGlobalModule -ErrorAction SilentlyContinue | Where-Object { $_.Name -match 'AspNetCore' }
    if ($mods) { $mods | ForEach-Object { [void]$sb.AppendLine("IIS_MODULE: $($_.Name) | $($_.Image)") } }
    else { [void]$sb.AppendLine("IIS AspNetCore modules: none listed (or WebAdministration unavailable)") }
} catch { [void]$sb.AppendLine("Get-WebGlobalModule: $($_.Exception.Message)") }

# Q–T SQL
[void]$sb.AppendLine((Section 'Q-T SQL Server'))
Get-Service -Name '*SQL*' -ErrorAction SilentlyContinue | ForEach-Object {
    [void]$sb.AppendLine("SERVICE: $($_.Name) | $($_.Status) | $($_.DisplayName)")
}
try {
    $inst = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server' -ErrorAction SilentlyContinue).InstalledInstances
    [void]$sb.AppendLine("InstalledInstances: $($inst -join ', ')")
} catch { [void]$sb.AppendLine("SQL instances registry: n/a") }

# Try local default instance list DBs without printing connection secrets
try {
    Add-Type -AssemblyName System.Data
    $csCandidates = @(
        'Server=localhost;Database=master;Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=5',
        'Server=.\SQLEXPRESS;Database=master;Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=5',
        'Server=(localdb)\MSSQLLocalDB;Database=master;Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=5'
    )
    $connected = $false
    foreach ($cs in $csCandidates) {
        try {
            $conn = New-Object System.Data.SqlClient.SqlConnection $cs
            $conn.Open()
            $cmd = $conn.CreateCommand()
            $cmd.CommandText = "SELECT name FROM sys.databases ORDER BY name"
            $r = $cmd.ExecuteReader()
            $names = @()
            while ($r.Read()) { $names += $r.GetString(0) }
            $r.Close(); $conn.Close()
            [void]$sb.AppendLine("SQL_CONNECT_OK using Windows auth candidate (server redacted pattern)")
            [void]$sb.AppendLine("DATABASES: $($names -join ', ')")
            [void]$sb.AppendLine("TiaanoVms_EXISTS: $($names -contains 'TiaanoVms')")
            $connected = $true
            break
        } catch {
            [void]$sb.AppendLine("SQL candidate failed: $($_.Exception.Message)")
        }
    }
    if (-not $connected) {
        [void]$sb.AppendLine("Could not list databases with local Windows auth. ConnectionStrings__DefaultConnection may point to remote SQL — check env PRESENT/MISSING next.")
    }
} catch {
    [void]$sb.AppendLine("SQL probe error: $($_.Exception.Message)")
}

# Resolve API root
[void]$sb.AppendLine((Section 'U-W API folder / permissions / contents'))
$apiRoot = $null
if ($apiApp -and $apiApp.PhysicalPath) { $apiRoot = [Environment]::ExpandEnvironmentVariables($apiApp.PhysicalPath) }
elseif ($sitePath) {
    $candidate = Join-Path ([Environment]::ExpandEnvironmentVariables($sitePath)) 'api'
    if (Test-Path $candidate) { $apiRoot = $candidate }
}
[void]$sb.AppendLine("API_ROOT_GUESSED: $apiRoot")

function Show-AclLine($path) {
    if (-not (Test-Path $path)) { return "MISSING: $path" }
    $acl = Get-Acl $path
    $lines = @("PATH: $path")
    foreach ($a in $acl.Access) {
        $lines += "  $($a.IdentityReference) | $($a.FileSystemRights) | $($a.AccessControlType) | Inherited=$($a.IsInherited)"
    }
    return ($lines -join "`r`n")
}

if ($apiRoot) {
    foreach ($p in @($apiRoot, (Join-Path $apiRoot 'App_Data'), (Join-Path $apiRoot 'wwwroot\branding'), (Join-Path $apiRoot 'logs'))) {
        [void]$sb.AppendLine((Show-AclLine $p))
    }
    [void]$sb.AppendLine("TOP_LEVEL_FILES:")
    Get-ChildItem $apiRoot -Force | Select-Object Name, Mode, Length | Format-Table -AutoSize | Out-String | ForEach-Object { [void]$sb.AppendLine($_) }
    [void]$sb.AppendLine("HAS_web.config: $(Test-Path (Join-Path $apiRoot 'web.config'))")
    [void]$sb.AppendLine("HAS_Tiaano.Vms.Api.dll: $(Test-Path (Join-Path $apiRoot 'Tiaano.Vms.Api.dll'))")
    [void]$sb.AppendLine("HAS_MlModels_det: $(Test-Path (Join-Path $apiRoot 'MlModels\det_10g.onnx'))")
    [void]$sb.AppendLine("HAS_MlModels_arc: $(Test-Path (Join-Path $apiRoot 'MlModels\w600k_r50.onnx'))")
}

# X web.config redacted
[void]$sb.AppendLine((Section 'X web.config (redacted)'))
if ($apiRoot -and (Test-Path (Join-Path $apiRoot 'web.config'))) {
    $raw = Get-Content (Join-Path $apiRoot 'web.config') -Raw
    [void]$sb.AppendLine((Redact-Text $raw))
}

# Y frontend web.config / rewrite
[void]$sb.AppendLine((Section 'Y frontend web.config / rewrite'))
if ($sitePath) {
    $front = [Environment]::ExpandEnvironmentVariables($sitePath)
    [void]$sb.AppendLine("FRONTEND_ROOT: $front")
    $fw = Join-Path $front 'web.config'
    [void]$sb.AppendLine("FRONTEND_web.config exists: $(Test-Path $fw)")
    if (Test-Path $fw) {
        [void]$sb.AppendLine((Redact-Text (Get-Content $fw -Raw)))
    }
    [void]$sb.AppendLine("login index? index.html=$(Test-Path (Join-Path $front 'index.html')) login folder=$(Test-Path (Join-Path $front 'login'))")
}

# Z URL Rewrite
[void]$sb.AppendLine((Section 'Z URL Rewrite installed?'))
[void]$sb.AppendLine("Rewrite module reg: $(Test-Path 'HKLM:\SOFTWARE\Microsoft\IIS Extensions\URL Rewrite')")
try {
    $rm = Get-WebGlobalModule -ErrorAction SilentlyContinue | Where-Object { $_.Name -match 'Rewrite' }
    if ($rm) { $rm | ForEach-Object { [void]$sb.AppendLine("IIS_MODULE: $($_.Name)") } }
    else { [void]$sb.AppendLine("No Rewrite module listed") }
} catch { [void]$sb.AppendLine("Rewrite check: $($_.Exception.Message)") }

# Env var PRESENT/MISSING only
[void]$sb.AppendLine((Section 'Environment variables PRESENT/MISSING (values never printed)'))
$keys = @(
    'ASPNETCORE_ENVIRONMENT',
    'ASPNETCORE_PATHBASE',
    'ConnectionStrings__DefaultConnection',
    'Jwt__Key',
    'Security__DataProtectionKey',
    'Seed__DefaultPassword',
    'Cors__Origins__0',
    'AllowedHosts'
)
foreach ($k in $keys) {
    $v = [Environment]::GetEnvironmentVariable($k, 'Machine')
    if ([string]::IsNullOrWhiteSpace($v)) { $v = [Environment]::GetEnvironmentVariable($k, 'Process') }
    if ([string]::IsNullOrWhiteSpace($v)) {
        [void]$sb.AppendLine("$k = MISSING")
    } else {
        $status = 'PRESENT'
        if ($k -eq 'Jwt__Key' -and $v.Length -lt 32) { $status = 'INVALID FORMAT (too short)' }
        if ($k -eq 'Security__DataProtectionKey' -and $v.Length -lt 16) { $status = 'INVALID FORMAT (too short)' }
        if ($k -eq 'ASPNETCORE_ENVIRONMENT' -and $v -ne 'Production') { $status = "PRESENT (value is not Production — length $($v.Length))" }
        if ($k -eq 'ASPNETCORE_PATHBASE' -and $v.TrimEnd('/') -ne '/api') { $status = "PRESENT (unexpected PathBase length $($v.Length))" }
        if ($k -match 'ConnectionStrings' -and ($v -match 'YOUR_|SET_VIA|REPLACE|CHANGE_ME')) { $status = 'INVALID FORMAT (placeholder)' }
        [void]$sb.AppendLine("$k = $status")
    }
}
[void]$sb.AppendLine("NOTE: IIS app-level env vars in web.config / Configuration Editor may not appear as Machine env — also inspect applicationHost / web.config environmentVariables (redacted above).")

# Quick HTTP probe (no secrets)
[void]$sb.AppendLine((Section 'HTTP probe'))
try {
    $resp = Invoke-WebRequest -Uri 'https://visitors.tiaanosoft.com/api/system/health' -UseBasicParsing -TimeoutSec 30
    [void]$sb.AppendLine("health StatusCode=$($resp.StatusCode) ContentType=$($resp.Headers['Content-Type']) Length=$($resp.RawContentLength)")
    $snippet = $resp.Content
    if ($snippet.Length -gt 400) { $snippet = $snippet.Substring(0, 400) }
    [void]$sb.AppendLine("body_snippet: $snippet")
} catch {
    [void]$sb.AppendLine("health request failed: $($_.Exception.Message)")
    if ($_.Exception.Response) {
        [void]$sb.AppendLine("response status: $([int]$_.Exception.Response.StatusCode)")
    }
}

$text = $sb.ToString()
$text = Redact-Text $text
Set-Content -Path $Report -Value $text -Encoding UTF8
Write-Host "Wrote $Report"
Write-Host "Open that file, review, and share a REDACTED copy if you want help with Phase 1+."
Write-Host "Do NOT paste Jwt/SQL/DataProtection values into chat."
