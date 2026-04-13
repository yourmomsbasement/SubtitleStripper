<#
.SYNOPSIS
    Builds and packages the SubtitleStripper plugin for a Jellyfin release.

.PARAMETER Version
    Version to build (default: reads from SubtitleStripper.csproj).

.EXAMPLE
    .\package.ps1
    .\package.ps1 -Version 1.1.0
#>
param(
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"
$ProjectDir  = $PSScriptRoot
$CsprojPath  = Join-Path $ProjectDir "SubtitleStripper.csproj"
$DllName     = "Jellyfin.Plugin.SubtitleStripper.dll"
$MetaPath    = Join-Path $ProjectDir "meta.json"
$ManifestPath = Join-Path $ProjectDir "manifest.json"

# ── Resolve version ────────────────────────────────────────────────────────────
if (-not $Version) {
    $xml = [xml](Get-Content $CsprojPath)
    $Version = $xml.Project.PropertyGroup.Version | Select-Object -First 1
    if (-not $Version) { $Version = "1.0.0" }
}
$Version4 = if ($Version -match '^\d+\.\d+\.\d+\.\d+$') { $Version } else { "$Version.0" }

$ZipName = "SubtitleStripper_$Version.zip"
$ZipPath = Join-Path $ProjectDir "bin\Release\$ZipName"

Write-Host "Building v$Version..." -ForegroundColor Cyan
dotnet build $CsprojPath -c Release /p:Version=$Version
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

$DllPath = Join-Path $ProjectDir "bin\Release\net9.0\$DllName"

# ── Update meta.json version ───────────────────────────────────────────────────
$meta = Get-Content $MetaPath | ConvertFrom-Json
$meta.version   = $Version4
$meta.timestamp = (Get-Date -Format "yyyy-MM-ddTHH:mm:ssZ")
$meta | ConvertTo-Json -Depth 5 | Set-Content $MetaPath -Encoding UTF8
Write-Host "Updated meta.json → $Version4"

# ── Create ZIP ─────────────────────────────────────────────────────────────────
if (Test-Path $ZipPath) { Remove-Item $ZipPath }
Compress-Archive -Path $DllPath, $MetaPath -DestinationPath $ZipPath
Write-Host "Created $ZipName"

# ── Compute MD5 ────────────────────────────────────────────────────────────────
$md5 = (Get-FileHash $ZipPath -Algorithm MD5).Hash.ToLower()
Write-Host "MD5: $md5"

# ── Print manifest entry ───────────────────────────────────────────────────────
$timestamp = (Get-Date -Format "yyyy-MM-ddTHH:mm:ssZ")
Write-Host ""
Write-Host "Add this version block to manifest.json (replace OWNER/REPO):" -ForegroundColor Yellow
Write-Host @"
{
  "version": "$Version4",
  "changelog": "...",
  "targetAbi": "10.9.0.0",
  "sourceUrl": "https://github.com/OWNER/REPO/releases/download/v$Version/$ZipName",
  "checksum": "$md5",
  "timestamp": "$timestamp"
}
"@
Write-Host ""
Write-Host "Release ZIP: $ZipPath" -ForegroundColor Green
