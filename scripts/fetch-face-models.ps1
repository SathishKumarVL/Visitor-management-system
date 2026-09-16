<#
.SYNOPSIS
    Fetches the InsightFace buffalo_l weights used for face recognition.

.DESCRIPTION
    The ONNX weights are ~180 MB and are deliberately not committed. Every machine that runs the API
    with face recognition enabled needs them on disk at backend/MlModels.

    Only two of the five models in the pack are used: det_10g.onnx (SCRFD detection) and
    w600k_r50.onnx (ArcFace embedding). The rest are removed after extraction.
#>
[CmdletBinding()]
param(
    [string]$Destination = (Join-Path $PSScriptRoot '..\backend\MlModels'),
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

$source = 'https://github.com/deepinsight/insightface/releases/download/v0.7/buffalo_l.zip'
$required = @('det_10g.onnx', 'w600k_r50.onnx')

$Destination = [System.IO.Path]::GetFullPath($Destination)
New-Item -ItemType Directory -Force -Path $Destination | Out-Null

$missing = $required | Where-Object { -not (Test-Path (Join-Path $Destination $_)) }
if (-not $missing -and -not $Force) {
    Write-Host "Face models already present in $Destination."
    exit 0
}

$archive = Join-Path $Destination 'buffalo_l.zip'
Write-Host "Downloading buffalo_l (~275 MB) from $source"
$ProgressPreference = 'SilentlyContinue'
Invoke-WebRequest -Uri $source -OutFile $archive -TimeoutSec 900

Write-Host 'Extracting'
Expand-Archive -Path $archive -DestinationPath $Destination -Force
Remove-Item $archive -Force

# The pack also ships landmark, gender/age and 3D models that this application never loads.
Get-ChildItem -Path $Destination -Filter '*.onnx' |
    Where-Object { $required -notcontains $_.Name } |
    Remove-Item -Force

foreach ($name in $required) {
    $path = Join-Path $Destination $name
    if (-not (Test-Path $path)) { throw "Expected $name in the archive but it was not extracted." }
    Write-Host ("  {0}  {1:N1} MB" -f $name, ((Get-Item $path).Length / 1MB))
}

Write-Host "Face models ready in $Destination."
