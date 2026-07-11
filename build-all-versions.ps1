#Requires -Version 5.1
<#
.SYNOPSIS
    Builds BeatSaberChromaGLS for one or all supported Beat Saber versions.

.DESCRIPTION
    Mirrors the way the Heck solution builds for multiple Beat Saber versions:
    each build uses a configuration named '<Configuration>-<Version>' and a
    BeatSaberDir passed to MSBuild.

    Before running this script, set the Beat Saber installation path for each
    version you want to build as an environment variable named
    BEATSABER_<Version> with dots replaced by underscores, e.g.:

        $env:BEATSABER_1_40_8 = "C:\Users\tdrak\BSManager\BSInstances\1.40.8"
        $env:BEATSABER_1_29_1 = "C:\Users\tdrak\BSManager\BSInstances\1.29.1"

    Make sure you have also added the Aeroluna GitHub Packages NuGet source to
    your user-level NuGet.config (see README.md).

.PARAMETER Version
    Specific Beat Saber version to build. If omitted, all supported versions
    that have environment variables set are built.

.PARAMETER Configuration
    MSBuild configuration to build: Debug or Release. Defaults to Release.
#>
param(
    [ValidateSet("1.29.1", "1.34.2", "1.37.1", "1.40.8", "1.42.1")]
    [string]$Version,

    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$SupportedVersions = @("1.29.1", "1.34.2", "1.37.1", "1.40.8", "1.42.1")
$SlnFile = Join-Path $PSScriptRoot "ChromaGLS.sln"

function Get-BeatSaberEnvVarName {
    param([string]$GameVersion)
    return "BEATSABER_" + $GameVersion.Replace(".", "_")
}

if (-not (Test-Path $SlnFile)) {
    Write-Error "Solution not found: $SlnFile"
    exit 1
}

$versionsToBuild = if ($Version) { @($Version) } else { $SupportedVersions }

# Validate that all required environment variables are set and point to existing directories.
$missing = @()
$invalid = @()
foreach ($ver in $versionsToBuild) {
    $envVarName = Get-BeatSaberEnvVarName -GameVersion $ver
    $path = [Environment]::GetEnvironmentVariable($envVarName, "Process")

    if ([string]::IsNullOrWhiteSpace($path)) {
        $missing += "    $envVarName  (Beat Saber $ver)"
        continue
    }

    if (-not (Test-Path $path)) {
        $invalid += "    $envVarName points to a path that does not exist: $path"
    }
}

if ($invalid.Count -gt 0) {
    Write-Error "Invalid Beat Saber installation paths:`n$($invalid -join "`n")"
    exit 1
}

if ($missing.Count -gt 0) {
    Write-Error @"
Missing Beat Saber installation environment variables.
Set each one to the root of the corresponding Beat Saber install:
$($missing -join "`n")

Example:
    `$env:BEATSABER_1_40_8 = 'C:\Users\tdrak\BSManager\BSInstances\1.40.8'
"@
    exit 1
}

$successfulBuilds = @()
$failedBuilds = @()

foreach ($ver in $versionsToBuild) {
    $envVarName = Get-BeatSaberEnvVarName -GameVersion $ver
    $beatSaberDir = [Environment]::GetEnvironmentVariable($envVarName, "Process")
    $buildConfig = "$Configuration-$ver"

    Write-Host ""
    Write-Host "=== Building $buildConfig ===" -ForegroundColor Cyan
    Write-Host "BeatSaberDir: $beatSaberDir"

    dotnet build $SlnFile `
        -c $buildConfig `
        "-p:BeatSaberDir=$beatSaberDir" `
        --nologo

    if ($LASTEXITCODE -ne 0) {
        $failedBuilds += $buildConfig
        Write-Host "Build failed for $buildConfig" -ForegroundColor Red
    } else {
        $successfulBuilds += $buildConfig
        Write-Host "Build succeeded for $buildConfig" -ForegroundColor Green
    }
}

Write-Host ""
Write-Host "=== Build summary ===" -ForegroundColor Cyan
if ($successfulBuilds.Count -gt 0) {
    Write-Host "Successful: $($successfulBuilds -join ", ")" -ForegroundColor Green
}
if ($failedBuilds.Count -gt 0) {
    Write-Host "Failed: $($failedBuilds -join ", ")" -ForegroundColor Red
    exit 1
}

Write-Host "All builds succeeded." -ForegroundColor Green
