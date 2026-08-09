#Requires -Version 5.1
# LogPose installer / updater for OPTCGSim.
#
# Quick install (PowerShell):
#   irm https://raw.githubusercontent.com/HVallad/LogPose/main/install.ps1 | iex
#
# Or from a downloaded copy:
#   powershell -ExecutionPolicy Bypass -File install.ps1 [-GamePath "C:\path\to\OPTCGSim"]
#
# Re-running updates LogPose to the newest release. -Uninstall removes the plugin
# (BepInEx is left in place).

[CmdletBinding()]
param(
    [string]$GamePath,
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
# GitHub requires TLS 1.2+; Windows PowerShell 5.1 doesn't enable it by default.
[Net.ServicePointManager]::SecurityProtocol = `
    [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

function Write-Step([string]$msg) { Write-Host "==> $msg" -ForegroundColor Cyan }

# --- Locate the game -------------------------------------------------------
if (-not $GamePath) {
    $candidates = @()
    if ($PSScriptRoot) { $candidates += $PSScriptRoot }   # empty when piped through iex
    $candidates += (Get-Location).Path
    $candidates += 'D:\OPSIM', 'C:\OPSIM', "$env:USERPROFILE\Desktop\OPTCGSim", "$env:USERPROFILE\Downloads\OPTCGSim"
    foreach ($c in $candidates) {
        if ($c -and (Test-Path (Join-Path $c 'OPTCGSim.exe'))) { $GamePath = $c; break }
    }
}
while (-not $GamePath -or -not (Test-Path (Join-Path $GamePath 'OPTCGSim.exe'))) {
    if ($GamePath) { Write-Host "OPTCGSim.exe not found in '$GamePath'." -ForegroundColor Yellow }
    $GamePath = Read-Host 'Enter your OPTCGSim folder (the one containing OPTCGSim.exe)'
}
$GamePath = (Resolve-Path $GamePath).Path
Write-Host "Game folder: $GamePath"

# --- The game must be closed (the loaded plugin DLL is locked) -------------
$proc = Get-Process OPTCGSim -ErrorAction SilentlyContinue
if ($proc) {
    $ans = Read-Host 'OPTCGSim is running and must be closed first. Close it now? [Y/n]'
    if ($ans -match '^[nN]') { throw 'Aborted - close the game and re-run this script.' }
    $proc | Stop-Process -Force
    Start-Sleep -Seconds 1
}

$pluginsDir = Join-Path $GamePath 'BepInEx\plugins'
$dllPath = Join-Path $pluginsDir 'LogPose.dll'

if ($Uninstall) {
    if (Test-Path $dllPath) {
        Remove-Item $dllPath -Force
        Write-Host 'LogPose.dll removed. BepInEx was left in place.' -ForegroundColor Green
    } else {
        Write-Host 'LogPose.dll not found - nothing to remove.'
    }
    return
}

$headers = @{ 'User-Agent' = 'LogPose-installer' }

# --- BepInEx (skip if already present) -------------------------------------
if (Test-Path (Join-Path $GamePath 'BepInEx\core\BepInEx.dll')) {
    Write-Step 'BepInEx already installed - skipping'
} else {
    Write-Step 'Fetching the latest BepInEx 5.x release info'
    $rel = Invoke-RestMethod 'https://api.github.com/repos/BepInEx/BepInEx/releases/latest' -Headers $headers
    $asset = $rel.assets | Where-Object {
        $_.name -match '^BepInEx.*win.*x64.*\.zip$' -or $_.name -match '^BepInEx_x64_.*\.zip$'
    } | Select-Object -First 1
    if (-not $asset) { throw "No win-x64 zip found in BepInEx release $($rel.tag_name)." }
    $zip = Join-Path $env:TEMP $asset.name
    Write-Step "Downloading $($asset.name)"
    Invoke-WebRequest $asset.browser_download_url -OutFile $zip -UseBasicParsing -Headers $headers
    Write-Step 'Extracting BepInEx into the game folder'
    Expand-Archive -Path $zip -DestinationPath $GamePath -Force
    Remove-Item $zip -Force
    if (-not (Test-Path (Join-Path $GamePath 'winhttp.dll'))) {
        throw 'BepInEx extraction failed (winhttp.dll missing in the game folder).'
    }
    Write-Host "BepInEx $($rel.tag_name) installed."
}

# --- LogPose ---------------------------------------------------------------
Write-Step 'Fetching the latest LogPose release info'
$rel = Invoke-RestMethod 'https://api.github.com/repos/HVallad/LogPose/releases/latest' -Headers $headers
$asset = $rel.assets | Where-Object { $_.name -eq 'LogPose.dll' } | Select-Object -First 1
if (-not $asset) { throw "LogPose release $($rel.tag_name) has no LogPose.dll asset." }
New-Item -ItemType Directory -Force -Path $pluginsDir | Out-Null
Write-Step "Downloading LogPose.dll ($($rel.tag_name))"
Invoke-WebRequest $asset.browser_download_url -OutFile $dllPath -UseBasicParsing -Headers $headers

Write-Host ''
Write-Host "LogPose $($rel.tag_name) installed." -ForegroundColor Green
Write-Host 'Start the game: Match History appears on the main menu; F6 opens the alt-art'
Write-Host 'selector in the deck editor. Config: BepInEx\config\com.hunter.logpose.cfg'
Write-Host 'Re-run this script anytime to update to the newest release.'
