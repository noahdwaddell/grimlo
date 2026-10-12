<#
.SYNOPSIS
    Builds and packages Grimlo for distribution.

.DESCRIPTION
    Creates a clean release package for Grimlo the Desktop Gremlin.
    Generates a distribution directory and zip archive in dist/.

.PARAMETER SelfContained
    If specified, bundles the entire .NET runtime (requires access to online NuGet package sources).

.EXAMPLE
    .\package.ps1
    Builds the standard portable release zip.

.EXAMPLE
    .\package.ps1 -SelfContained
    Builds the standalone runtime-bundled release zip.
#>
param(
    [switch]$SelfContained = $false,
    [string]$Configuration = "Release",
    [string]$Version = "1.0.0"
)

$ErrorActionPreference = "Stop"
$rootDir = $PSScriptRoot
$projectPath = Join-Path $rootDir "Grimlo\Grimlo.csproj"
$distDir = Join-Path $rootDir "dist"
$outputFolderName = if ($SelfContained) { "Grimlo-v$Version-standalone-win-x64" } else { "Grimlo-v$Version-win-x64" }
$stageDir = Join-Path $distDir $outputFolderName
$zipPath = Join-Path $distDir "$outputFolderName.zip"

Write-Host "========================================" -ForegroundColor Green
Write-Host " Grimlo Packaging & Distribution Script " -ForegroundColor Green
Write-Host " Version: $Version" -ForegroundColor Yellow
Write-Host " Mode:    $(if ($SelfContained) { 'Self-Contained (Runtime Included)' } else { 'Portable / Framework-Dependent' })" -ForegroundColor Yellow
Write-Host "========================================" -ForegroundColor Green

# 1. Clean previous stage
if (Test-Path $stageDir) {
    Remove-Item -Path $stageDir -Recurse -Force
}
if (Test-Path $zipPath) {
    Remove-Item -Path $zipPath -Force
}
New-Item -ItemType Directory -Path $stageDir -Force | Out-Null

# 2. Restore dependencies
Write-Host "`n[1/5] Restoring project dependencies..." -ForegroundColor Cyan
$nugetSource1 = "$env:USERPROFILE\.nuget\packages"
$nugetSource2 = "C:\Program Files (x86)\Microsoft SDKs\NuGetPackages\"
$restoreSources = @()
if (Test-Path $nugetSource1) { $restoreSources += "-s", $nugetSource1 }
if (Test-Path $nugetSource2) { $restoreSources += "-s", $nugetSource2 }

if ($SelfContained) {
    & dotnet restore $projectPath -r win-x64
} else {
    & dotnet restore $projectPath @restoreSources
}
if ($LASTEXITCODE -ne 0) {
    throw "Restore failed with exit code $LASTEXITCODE"
}

# 3. Build & Publish
Write-Host "[2/5] Publishing Grimlo ($Configuration)..." -ForegroundColor Cyan
if ($SelfContained) {
    & dotnet publish $projectPath -c $Configuration -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=none -p:DebugSymbols=false `
        -o $stageDir
} else {
    & dotnet publish $projectPath -c $Configuration --no-restore `
        -p:DebugType=none -p:DebugSymbols=false `
        -o $stageDir
}
if ($LASTEXITCODE -ne 0) {
    throw "Publish failed with exit code $LASTEXITCODE"
}

# 4. Clean debug artifacts
Write-Host "[3/5] Cleaning debug symbols..." -ForegroundColor Cyan
Get-ChildItem -Path $stageDir -Filter "*.pdb" -Recurse | Remove-Item -Force -ErrorAction SilentlyContinue

# 5. Generate distribution README.txt
Write-Host "[4/5] Adding release documentation..." -ForegroundColor Cyan
$readmeContent = @"
============================================================
  GRIMLO THE DESKTOP GREMLIN - v$Version
============================================================

Your eccentric, melodramatic desktop gremlin assistant.

QUICK START:
1. Double-click 'Grimlo.exe' to launch Grimlo.
2. Grimlo will float on your desktop above the taskbar.
3. Left-click Grimlo to chat or give commands.
4. Left-click and drag to move him anywhere on your screen.
5. Right-click Grimlo for quick actions and Settings.

CHAT SLASH COMMANDS:
Click Grimlo to open the chat bar, type any command, and press Enter:
  /think [prompt] - Deep contemplation and elaborate monologues.
  /long [prompt]  - Alias for /think (in-depth multi-paragraph response).
  /look [prompt]  - Screen capture & visual critique of your desktop.
  /judge          - Harsh roast of your active foreground window.
  /listen         - Critique whatever music or audio is playing.
  /wander         - Takes an animated patrol along your taskbar.
  /sleep          - Put Grimlo to sleep immediately.
  /tap            - Tap the monitor glass from the inside.
  /shake          - Make Grimlo dizzy with complaining.
  /clear          - Wipe conversation memory.
  /help           - Recite available commands.
  /commands       - Alias for /help.
  Esc             - Close the chat bar.

AI SETUP (FREE API KEY):
Right-click Grimlo -> Settings...
You can get a free Google Gemini API key at:
https://aistudio.google.com/api-keys
Paste the key in the settings tab and hit Save!

CONTROLS & SHORTCUTS:
- Drag Grimlo quickly back and forth while holding left click to shake him dizzy!
- Click 'Pause' or 'Quiet' on-screen while Grimlo speaks to control speech.
- Click the 'X' in the top-right of the speech bubble to dismiss text early.
- Right-click Grimlo -> Help & Commands for the full guide.

CREDITS & LICENSE:
- App designed by Noah Waddell of ellijaylabs.com (MIT License).
- Original character artwork created by Kris Sanford (All Rights Reserved).
- See LICENSE file for full legal terms.

SYSTEM REQUIREMENTS:
- Windows 10 or Windows 11 (64-bit)
$(if (!$SelfContained) { "- Microsoft .NET 8 Desktop Runtime (https://dotnet.microsoft.com/download/dotnet/8.0)" })

Enjoy your new resident desktop gremlin!
============================================================
"@
Set-Content -Path (Join-Path $stageDir "README.txt") -Value $readmeContent -Encoding utf8

# Copy LICENSE into distribution folder
$licenseSrc = Join-Path $rootDir "LICENSE"
if (Test-Path $licenseSrc) {
    Copy-Item -Path $licenseSrc -Destination (Join-Path $stageDir "LICENSE") -Force
}

# 6. Create Distribution Zip Archive
Write-Host "[5/5] Creating distribution archive: $zipPath..." -ForegroundColor Cyan
Compress-Archive -Path "$stageDir\*" -DestinationPath $zipPath -Force

Write-Host "`nPackage complete!" -ForegroundColor Green
Write-Host "Folder:  $stageDir" -ForegroundColor White
Write-Host "Archive: $zipPath" -ForegroundColor White
$zipSize = (Get-Item $zipPath).Length / 1MB
Write-Host "Size:    $([math]::Round($zipSize, 2)) MB" -ForegroundColor Yellow
