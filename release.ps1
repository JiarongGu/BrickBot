# GitHub Release Helper Script for BrickBot
# Bumps version in .csproj, runs production build, creates release package + notes,
# optionally creates a git tag.
#
# Usage:
#   .\release.ps1                           # Use current version
#   .\release.ps1 -Version "1.2"            # Set specific version
#   .\release.ps1 -BumpMajor                # 1.0 -> 2.0
#   .\release.ps1 -BumpMinor                # 1.0 -> 1.1
#   .\release.ps1 -SkipBuild                # Reuse existing publish/
#   .\release.ps1 -CreateTag                # Auto create git tag

param(
    [string]$Version,
    [switch]$BumpMajor,
    [switch]$BumpMinor,
    [switch]$SkipBuild,
    [switch]$CreateTag,
    [ValidateSet("win-x64", "win-x86", "all")]
    [string]$Platform = "all"
)

$ErrorActionPreference = "Stop"

function Get-ProjectVersion {
    $csprojPath = "BrickBot\BrickBot.csproj"
    if (-not (Test-Path $csprojPath)) {
        throw "Could not find .csproj file at $csprojPath"
    }
    [xml]$csproj = Get-Content $csprojPath
    $versionNode = $csproj.Project.PropertyGroup.Version | Where-Object { $_ -ne $null } | Select-Object -First 1
    if (-not $versionNode) {
        throw "No <Version> element found in .csproj"
    }
    return $versionNode
}

function Set-ProjectVersion {
    param([string]$NewVersion)

    $csprojPath = "BrickBot\BrickBot.csproj"
    [xml]$csproj = Get-Content $csprojPath
    $propertyGroup = $csproj.Project.PropertyGroup | Where-Object { $_.Version -ne $null } | Select-Object -First 1
    if (-not $propertyGroup) {
        throw "No <Version> element found in .csproj"
    }
    $propertyGroup.Version = $NewVersion
    $propertyGroup.AssemblyVersion = "$NewVersion.0"
    $propertyGroup.FileVersion = "$NewVersion.0"
    $propertyGroup.InformationalVersion = $NewVersion
    $csproj.Save($csprojPath)
    Write-Host "  Updated version in .csproj to $NewVersion" -ForegroundColor Green
}

function Get-BumpedVersion {
    param([string]$CurrentVersion, [string]$BumpType)

    # Accept X.Y or X.Y.Z; bump on the first two segments only
    if ($CurrentVersion -notmatch '^(\d+)\.(\d+)') {
        throw "Invalid version format: $CurrentVersion"
    }
    $major = [int]$matches[1]
    $minor = [int]$matches[2]
    switch ($BumpType) {
        "major" { $major++; $minor = 0 }
        "minor" { $minor++ }
    }
    return "$major.$minor.0"
}

function Get-ReleaseNotes {
    param([string]$Version)

    $currentTag = "v$Version"
    $allTags = git tag --sort=-version:refname | Where-Object { $_ -match '^v\d' }
    $previousTag = $null
    foreach ($t in $allTags) {
        if ($t -ne $currentTag) { $previousTag = $t; break }
    }

    if ($previousTag) {
        Write-Host "  Generating notes: $previousTag..$currentTag" -ForegroundColor Gray
        $commits = git log "$previousTag..HEAD" --pretty=format:"%s" --no-merges
    } else {
        Write-Host "  No previous tag found - using recent 50 commits" -ForegroundColor Gray
        $commits = git log -50 --pretty=format:"%s" --no-merges
    }

    $features = @()
    $fixes = @()
    $other = @()

    foreach ($msg in $commits) {
        if ($msg -match '^feat:\s*(.+)') { $features += $matches[1].Trim() }
        elseif ($msg -match '^fix:\s*(.+)') { $fixes += $matches[1].Trim() }
        elseif ($msg -match '^(chore|docs|refactor|style|perf|test|ci):\s*') { } # skip
        else { $other += $msg }
    }

    $notes = @()
    if ($features.Count -gt 0) { $notes += "### New Features"; $notes += ""; foreach ($f in $features) { $notes += "- $f" }; $notes += "" }
    if ($fixes.Count -gt 0) { $notes += "### Bug Fixes"; $notes += ""; foreach ($f in $fixes) { $notes += "- $f" }; $notes += "" }
    if ($other.Count -gt 0) { $notes += "### Other Changes"; $notes += ""; foreach ($f in $other) { $notes += "- $f" }; $notes += "" }

    if ($notes.Count -eq 0) { return "No notable changes in this release." }
    return ($notes -join "`n")
}

# ========================================
# Main
# ========================================

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  BrickBot Release Helper" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$currentVersion = Get-ProjectVersion
Write-Host "Current version: $currentVersion" -ForegroundColor Gray
Write-Host ""

if ($BumpMajor -or $BumpMinor) {
    if ($Version) {
        Write-Host "Warning: -Version overrides -Bump*" -ForegroundColor Yellow
    } else {
        $bumpType = if ($BumpMajor) { "major" } else { "minor" }
        $Version = Get-BumpedVersion -CurrentVersion $currentVersion -BumpType $bumpType
        Write-Host "Bumping $bumpType version: $currentVersion -> $Version" -ForegroundColor Yellow
        Set-ProjectVersion -NewVersion $Version
    }
}

if (-not $Version) {
    $Version = $currentVersion
    Write-Host "Using current version: $Version" -ForegroundColor Yellow
} elseif ($Version -ne $currentVersion) {
    Write-Host "Setting version to: $Version (was $currentVersion)" -ForegroundColor Yellow
    Set-ProjectVersion -NewVersion $Version
}

$releaseTag = "v$Version"
Write-Host "Release tag: $releaseTag" -ForegroundColor Cyan
Write-Host ""

# Git status check
$gitStatus = git status --porcelain
if ($gitStatus) {
    Write-Host "WARNING: uncommitted changes:" -ForegroundColor Yellow
    Write-Host $gitStatus -ForegroundColor Gray
    $continue = Read-Host "Continue anyway? (y/N)"
    if ($continue -ne "y" -and $continue -ne "Y") { Write-Host "Aborted." -ForegroundColor Red; exit 1 }
}

$existingTag = git tag -l $releaseTag
if ($existingTag) {
    Write-Host "WARNING: Tag $releaseTag already exists" -ForegroundColor Yellow
    $continue = Read-Host "Continue anyway? (y/N)"
    if ($continue -ne "y" -and $continue -ne "Y") { Write-Host "Aborted." -ForegroundColor Red; exit 1 }
}

# Build
if (-not $SkipBuild) {
    Write-Host "Running production build..." -ForegroundColor Yellow
    & .\build-production.ps1 -Platform $Platform
    if ($LASTEXITCODE -ne 0) { Write-Host "Build failed!" -ForegroundColor Red; exit 1 }
} else {
    Write-Host "Skipping build (using existing publish/)" -ForegroundColor Yellow
}

# Package release zips
Write-Host ""
Write-Host "Creating release package..." -ForegroundColor Yellow

$releasePath = "release"
if (Test-Path $releasePath) { Remove-Item -Recurse -Force $releasePath }
New-Item -ItemType Directory -Path $releasePath -Force | Out-Null

$platforms = if ($Platform -eq "all") { @("win-x64", "win-x86") } else { @($Platform) }

foreach ($plat in $platforms) {
    $publishDir = "publish\$plat"
    if (-not (Test-Path $publishDir)) {
        Write-Host "  Warning: $publishDir not found, skipping" -ForegroundColor Yellow
        continue
    }
    $zipName = "BrickBot-$releaseTag-$plat.zip"
    $zipPath = Join-Path $releasePath $zipName
    Compress-Archive -Path "$publishDir\*" -DestinationPath $zipPath -Force
    $zipSize = [math]::Round((Get-Item $zipPath).Length / 1MB, 2)
    Write-Host "  Created $zipName ($zipSize MB)" -ForegroundColor Green
}

# Release notes
Write-Host ""
Write-Host "Generating release notes..." -ForegroundColor Yellow
$releaseNotes = Get-ReleaseNotes -Version $Version
$releaseNotesPath = Join-Path $releasePath "RELEASE_NOTES.md"

$fullReleaseNotes = @"
# BrickBot $releaseTag

## What's New

$releaseNotes

## Installation

1. Download ``BrickBot-$releaseTag-win-x64.zip``
2. Extract to a folder
3. Run ``BrickBot.exe``

**Requirements**: Windows 10/11 (x64), .NET 10 runtime (framework-dependent build)

## Package Contents

- ``BrickBot.exe`` - Main application
- ``data/languages/`` - Translation files (editable)
"@

$fullReleaseNotes | Out-File -FilePath $releaseNotesPath -Encoding UTF8
Write-Host "  Saved to release/RELEASE_NOTES.md" -ForegroundColor Green
Write-Host ""

# Summary
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Release Package Ready" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Version: $Version" -ForegroundColor Yellow
Write-Host "Tag: $releaseTag" -ForegroundColor Yellow
Write-Host ""
Write-Host "Files in release/:" -ForegroundColor Cyan
Get-ChildItem $releasePath -File | ForEach-Object {
    $size = [math]::Round($_.Length / 1MB, 2)
    Write-Host "  $($_.Name) ($size MB)" -ForegroundColor White
}
Write-Host ""

if ($CreateTag) {
    git tag -a $releaseTag -m "Release $releaseTag"
    if ($LASTEXITCODE -ne 0) { Write-Host "Failed to create git tag" -ForegroundColor Red; exit 1 }
    Write-Host "Git tag created: $releaseTag" -ForegroundColor Green
    Write-Host "Push tag with: git push origin $releaseTag" -ForegroundColor Cyan
} else {
    Write-Host "Next Steps:" -ForegroundColor Cyan
    Write-Host "  git tag -a $releaseTag -m `"Release $releaseTag`"" -ForegroundColor White
    Write-Host "  git push origin $releaseTag" -ForegroundColor White
}
Write-Host ""
Write-Host "Release preparation complete!" -ForegroundColor Green
