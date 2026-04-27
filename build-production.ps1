# Production Build Script for BrickBot
# Builds both the React frontend and .NET backend with single-file packaging
#
# Parameters:
#   -Platform: Target platform (win-x64, win-x86, or all) - Default: win-x64
#   -SelfContained: Build as self-contained (includes .NET runtime) - Default: $false
#   -SkipFrontend: Skip React frontend build - Default: $false
#
# Examples:
#   .\build-production.ps1                                    # x64 framework-dependent
#   .\build-production.ps1 -Platform win-x86                  # x86 framework-dependent
#   .\build-production.ps1 -Platform all                      # both x64 and x86
#   .\build-production.ps1 -SelfContained $true               # x64 self-contained

param(
    [ValidateSet("win-x64", "win-x86", "all")]
    [string]$Platform = "win-x64",

    [bool]$SelfContained = $false,

    [bool]$SkipFrontend = $false
)

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  BrickBot Production Build" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Configuration:" -ForegroundColor Yellow
Write-Host "  Platform: $Platform" -ForegroundColor White
Write-Host "  Self-Contained: $SelfContained" -ForegroundColor White
Write-Host "  Skip Frontend: $SkipFrontend" -ForegroundColor White
Write-Host ""

$platforms = @()
if ($Platform -eq "all") {
    $platforms = @("win-x64", "win-x86")
} else {
    $platforms = @($Platform)
}

# Step 1: Build React Frontend
if (-not $SkipFrontend) {
    Write-Host "[1/3] Building React frontend..." -ForegroundColor Yellow
    Set-Location BrickBot.Client

    if (Test-Path "build") {
        Remove-Item -Recurse -Force build
    }

    npm run build

    if ($LASTEXITCODE -ne 0) {
        Write-Host "X Frontend build failed!" -ForegroundColor Red
        Set-Location ..
        exit 1
    }

    Write-Host "- Frontend built successfully" -ForegroundColor Green
    Write-Host ""

    # Step 2: Copy build to backend wwwroot
    Write-Host "[2/3] Copying frontend build to backend..." -ForegroundColor Yellow
    Set-Location ..

    $wwwrootPath = "BrickBot\wwwroot"

    if (Test-Path $wwwrootPath) {
        Remove-Item -Recurse -Force $wwwrootPath
    }

    New-Item -ItemType Directory -Path $wwwrootPath -Force | Out-Null
    Copy-Item -Path "BrickBot.Client\build\*" -Destination $wwwrootPath -Recurse -Force

    Write-Host "- Frontend copied to wwwroot (will be embedded as resources)" -ForegroundColor Green
    Write-Host ""
} else {
    Write-Host "[1/3] Skipping frontend build (using existing wwwroot)" -ForegroundColor Yellow
    Write-Host "[2/3] Skipping frontend copy" -ForegroundColor Yellow
    Write-Host ""
}

# Step 3: Publish .NET Application for each platform
Write-Host "[3/3] Publishing .NET application..." -ForegroundColor Yellow
Set-Location BrickBot

$totalPlatforms = $platforms.Count
$currentPlatform = 0

foreach ($plat in $platforms) {
    $currentPlatform++
    Write-Host ""
    Write-Host "  [$currentPlatform/$totalPlatforms] Building for $plat..." -ForegroundColor Cyan

    $runtimeIdentifier = $plat
    $outputPath = "bin\Release\net10.0-windows10.0.19041.0\$runtimeIdentifier\publish"

    if (Test-Path $outputPath) {
        Remove-Item -Recurse -Force $outputPath
    }

    $publishArgs = @(
        "publish",
        "-c", "Release",
        "-r", $runtimeIdentifier,
        "-o", $outputPath,
        "/p:PublishReadyToRun=true",
        "/p:PublishTrimmed=false"
    )

    if ($SelfContained) {
        $publishArgs += "/p:PublishSingleFile=true"
        $publishArgs += "/p:IncludeAllContentForSelfExtract=false"
        $publishArgs += "--self-contained"
    } else {
        $publishArgs += "--no-self-contained"
    }

    Write-Host "    Running: dotnet $($publishArgs -join ' ')" -ForegroundColor DarkGray

    & dotnet $publishArgs

    if ($LASTEXITCODE -ne 0) {
        Write-Host "    X .NET publish failed for $plat!" -ForegroundColor Red
        Set-Location ..
        exit 1
    }

    $exePath = Join-Path $outputPath "BrickBot.exe"
    if (Test-Path $exePath) {
        $exeSize = [math]::Round((Get-Item $exePath).Length / 1MB, 2)
        Write-Host "    - Published successfully ($exeSize MB)" -ForegroundColor Green

        $fileCount = (Get-ChildItem -Path $outputPath -File).Count
        Write-Host "    Output contains $fileCount files" -ForegroundColor Gray
    } else {
        Write-Host "    X Executable not found!" -ForegroundColor Red
        Set-Location ..
        exit 1
    }
}

Set-Location ..

Write-Host ""
Write-Host "- All platforms built successfully" -ForegroundColor Green
Write-Host ""

# Organize publish/ folder
Write-Host "Organizing publish files..." -ForegroundColor Yellow

$publishPath = Join-Path $PSScriptRoot "publish"
if (Test-Path $publishPath) {
    Remove-Item -Recurse -Force $publishPath
}
New-Item -ItemType Directory -Path $publishPath -Force | Out-Null

foreach ($plat in $platforms) {
    $sourcePath = Join-Path $PSScriptRoot "BrickBot\bin\Release\net10.0-windows10.0.19041.0\$plat\publish"
    $destPath = Join-Path $PSScriptRoot "publish\$plat"

    New-Item -ItemType Directory -Path $destPath -Force | Out-Null

    # Copy main executable
    Copy-Item -Path "$sourcePath\BrickBot.exe" -Destination $destPath -Force

    # Framework-dependent builds need the .dll alongside the exe
    if (-not $SelfContained -and (Test-Path "$sourcePath\BrickBot.dll")) {
        Copy-Item -Path "$sourcePath\BrickBot.dll" -Destination $destPath -Force
        Copy-Item -Path "$sourcePath\BrickBot.runtimeconfig.json" -Destination $destPath -Force -ErrorAction SilentlyContinue
        Copy-Item -Path "$sourcePath\BrickBot.deps.json" -Destination $destPath -Force -ErrorAction SilentlyContinue
    }

    # Copy language files (kept separate for easy user modification)
    if (Test-Path "$sourcePath\data\languages") {
        $langDestPath = Join-Path $destPath "data\languages"
        New-Item -ItemType Directory -Path $langDestPath -Force | Out-Null
        Copy-Item -Path "$sourcePath\data\languages\*" -Destination $langDestPath -Force
    }

    Write-Host "  $plat packaged to publish\$plat" -ForegroundColor Green
}

Write-Host ""
Write-Host "Publish files organized" -ForegroundColor Green
Write-Host ""

# Summary
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Build Summary" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Build Type: $(if ($SelfContained) { 'Self-Contained' } else { 'Framework-Dependent' })" -ForegroundColor Yellow
Write-Host ""

foreach ($plat in $platforms) {
    $publishPlatformPath = Join-Path $PSScriptRoot "publish\$plat"
    $exePath = Join-Path $publishPlatformPath "BrickBot.exe"

    if (Test-Path $exePath) {
        $exeSize = [math]::Round((Get-Item $exePath).Length / 1MB, 2)
        $fileCount = (Get-ChildItem -Path $publishPlatformPath -Recurse -File).Count

        Write-Host "Platform: $plat" -ForegroundColor Cyan
        Write-Host "  Location: publish\$plat\" -ForegroundColor White
        Write-Host "  Executable Size: $exeSize MB" -ForegroundColor White
        Write-Host "  Total Files: $fileCount" -ForegroundColor White
        Write-Host ""
    }
}

if (-not $SelfContained) {
    Write-Host "Note: Framework-dependent build requires .NET 10 runtime to be installed" -ForegroundColor Yellow
    Write-Host ""
}

Write-Host "To run the application:" -ForegroundColor Cyan
Write-Host "  cd publish\$($platforms[0])\" -ForegroundColor White
Write-Host "  .\BrickBot.exe" -ForegroundColor White
Write-Host ""
Write-Host "- Build completed successfully!" -ForegroundColor Green
