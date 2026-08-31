#Requires -Version 7.0
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

        $env:BEATSABER_1_40_8 = "C:\Users\{you}\BSManager\BSInstances\1.40.8"
        $env:BEATSABER_1_29_1 = "C:\Users\{you}\BSManager\BSInstances\1.29.1"

    Make sure you have also added the Aeroluna GitHub Packages NuGet source to
    your user-level NuGet.config (see README.md).

.PARAMETER Version
    Specific Beat Saber version to build. If omitted, all supported versions
    that have environment variables set are built.

.PARAMETER Release
    Build Release configurations instead of the default Debug configurations.

#>
param(
    [ValidateSet("1.29.1", "1.34.2", "1.37.1", "1.40.8", "1.42.1", "1.44.1", "1.44.2")]
    [string]$Version = $null,

    [switch]$Release,

    # Match the project fallback so local builds cannot numerically downgrade an installed plugin.
    # The build task adds the Beat Saber version as SemVer build metadata.
    [ValidatePattern('^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$')]
    [string]$PluginVersion = "1.0.3"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Repository-local cleanup keeps queued BSIPA deployments from outranking a later direct multi-version build.
. (Join-Path $PSScriptRoot "ScriptCommon.ps1")

# Default local builds to Debug; CI and packaging pass -Release explicitly.
$Configuration = "Debug"
if ($Release) {
    $Configuration = "Release"
}
$SupportedVersions = @("1.29.1", "1.34.2", "1.37.1", "1.40.8", "1.42.1", "1.44.1", "1.44.2")
# $SupportedVersions = @("1.29.1", "1.40.8")
$SlnFile = Join-Path $PSScriptRoot "ChromaGLS.sln"

function Get-BeatSaberEnvVarName {
    param([string]$GameVersion)
    return "BEATSABER_" + $GameVersion.Replace(".", "_")
}

if (-not (Test-Path $SlnFile)) {
    Write-Error "Solution not found: $SlnFile"
    exit 1
}

$versionsToBuild = if ($Version) { 
    @($Version) 
} else { 
    @(
        $SupportedVersions | 
            ? { $_ -ne "1.44.2" }  # 1.44.2 doesn't have most of the necessary dependencies currently, due to api changes breaking them. CJD and BS_Utils appear broken atm
    ) 
}

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
    `$env:BEATSABER_1_40_8 = 'C:\Users\{you}\BSManager\BSInstances\1.40.8'
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
        "-p:Version=$PluginVersion" `
        --nologo

    if ($LASTEXITCODE -ne 0) {
        $failedBuilds += $buildConfig
        Write-Host "Build failed for $buildConfig" -ForegroundColor Red
        Write-Host "Built DLL timestamp: unavailable because the build failed" -ForegroundColor Yellow
    } else {
        $successfulBuilds += $buildConfig
        Write-Host "Build succeeded for $buildConfig" -ForegroundColor Green

        # Report the filesystem timestamp from the exact DLL produced by this configuration.
        # MSBuild places the target-framework output below the configuration directory.
        $builtDllPath = Join-Path $PSScriptRoot "ChromaGLS\bin\$buildConfig\net48\ChromaGLS.dll"
        if (Test-Path $builtDllPath) {
            $builtDll = Get-Item -LiteralPath $builtDllPath
            Write-Host "Built DLL: $($builtDll.FullName)" -ForegroundColor Green
            Write-Host "Built DLL timestamp (UTC): $($builtDll.LastWriteTimeUtc.ToString('O'))" -ForegroundColor Green

            # BSMT has now deployed either live or pending; discard only a pending ChromaGLS older than the live copy.
            Remove-StaleBsipaPendingPlugin -GameDirectory $beatSaberDir -PluginFileName "ChromaGLS.dll"
        } else {
            Write-Host "Built DLL timestamp: unavailable; expected artifact not found at $builtDllPath" -ForegroundColor Yellow
        }
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
