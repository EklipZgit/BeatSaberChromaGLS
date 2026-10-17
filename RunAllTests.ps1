#Requires -Version 7.0
<#
.SYNOPSIS
    Builds ChromaGLS and runs its NUnitLite suite under Unity's bundled Mono for one or all supported Beat Saber versions.

.DESCRIPTION
    ChromaGLS.Tests is a self-running net48 exe because only Unity's bundled Mono can host the game's
    MonoMod detours: net8 crashes MonoMod's self-test and desktop net48 cannot load CustomJSONData's
    default interface members. Each matrix entry first runs BuildAllVersions.ps1 for Release-<version>
    so the tested plugin and embedded ferrofluid bundle always match the current sources, then executes
    ChromaGLS.Tests.exe from that configuration's output directory.

    MonoBleedingEdge can crash during process teardown after results are written, so each entry's
    pass/fail comes from the freshly written TestResult.xml rather than the mono exit code.

.PARAMETER Version
    Specific Beat Saber version to test. If omitted, all supported versions are tested except 1.44.2,
    whose current installation lacks required mod dependencies.

.PARAMETER MonoPath
    Path to a Unity editor's bundled mono executable. Defaults to the newest Unity Hub editor's
    MonoBleedingEdge runtime.

.PARAMETER Test
    Optional NUnitLite --test filter: a comma-separated list of fully qualified test names.
#>
param(
    [ValidateSet("1.29.1", "1.34.2", "1.37.1", "1.40.8", "1.42.1", "1.44.1", "1.44.2")]
    [string]$Version = $null,

    [string]$MonoPath = $null,

    [string]$Test = $null
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$BuildScript = Join-Path $PSScriptRoot "BuildAllVersions.ps1"
$SupportedVersions = @("1.29.1", "1.34.2", "1.37.1", "1.40.8", "1.42.1", "1.44.1", "1.44.2")
$VersionsToTest = if ($Version) {
    @($Version)
}
else {
    $SupportedVersions | Where-Object { $_ -ne "1.44.2" }
}

if ([string]::IsNullOrWhiteSpace($MonoPath)) {
    $hubRoot = Join-Path $env:ProgramFiles "Unity\Hub\Editor"
    $MonoPath = Get-ChildItem -LiteralPath $hubRoot -Directory -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending |
        ForEach-Object { Join-Path $_.FullName "Editor\Data\MonoBleedingEdge\bin\mono.exe" } |
        Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
        Select-Object -First 1
}
if ([string]::IsNullOrWhiteSpace($MonoPath) -or -not (Test-Path -LiteralPath $MonoPath -PathType Leaf)) {
    Write-Error "No Unity Hub editor with a MonoBleedingEdge runtime found; pass -MonoPath explicitly."
    exit 1
}
Write-Host "Test host: $MonoPath" -ForegroundColor DarkGray

$SuccessfulTests = @()
$FailedTests = @()

foreach ($ver in $VersionsToTest) {
    $envVarName = "BEATSABER_" + $ver.Replace(".", "_")
    $beatSaberDir = [Environment]::GetEnvironmentVariable($envVarName, "Process")
    $configuration = "Release-$ver"

    # A missing installation or dependency is a failed matrix entry rather than a silent skip.
    if ([string]::IsNullOrWhiteSpace($beatSaberDir) -or -not (Test-Path -LiteralPath $beatSaberDir -PathType Container)) {
        Write-Host "[$configuration] Missing or invalid $envVarName." -ForegroundColor Red
        $FailedTests += $configuration
        continue
    }

    Write-Host ""
    Write-Host "=== Testing $configuration ===" -ForegroundColor Cyan

    # Isolate the build script's exit code so one failed target does not prevent the remaining matrix entries from reporting results.
    & pwsh -NoProfile -File $BuildScript -Version $ver -Release
    if ($LASTEXITCODE -ne 0) {
        $FailedTests += $configuration
        continue
    }

    $testDir = Join-Path $PSScriptRoot "ChromaGLS.Tests\bin\$configuration\net48"
    $testExe = Join-Path $testDir "ChromaGLS.Tests.exe"
    $resultsPath = Join-Path $testDir "TestResult.xml"
    if (-not (Test-Path -LiteralPath $testExe -PathType Leaf)) {
        Write-Host "[$configuration] Test host not found at $testExe." -ForegroundColor Red
        $FailedTests += $configuration
        continue
    }

    $runStartedUtc = [DateTime]::UtcNow
    $testArguments = @($testExe)
    if (-not [string]::IsNullOrWhiteSpace($Test)) {
        $testArguments += "--test=$Test"
    }

    # The harness resolves its game-assembly references beside the exe and writes TestResult.xml to the working directory.
    Push-Location -LiteralPath $testDir
    try {
        & $MonoPath @testArguments
        $monoExitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }

    $resultsFresh = (Test-Path -LiteralPath $resultsPath -PathType Leaf) -and
        ((Get-Item -LiteralPath $resultsPath).LastWriteTimeUtc -ge $runStartedUtc.AddSeconds(-5))
    if (-not $resultsFresh) {
        Write-Host "[$configuration] NUnitLite produced no fresh TestResult.xml (mono exit $monoExitCode)." -ForegroundColor Red
        $FailedTests += $configuration
        continue
    }

    if ($monoExitCode -ne 0) {
        Write-Host "[$configuration] mono exited $monoExitCode after writing results; trusting TestResult.xml." -ForegroundColor Yellow
    }

    [xml]$results = Get-Content -LiteralPath $resultsPath
    $run = $results.SelectSingleNode("/test-run")
    if ([int]$run.GetAttribute("total") -gt 0 -and [int]$run.GetAttribute("failed") -eq 0) {
        $SuccessfulTests += $configuration
    }
    else {
        $FailedTests += $configuration
    }
}

Write-Host ""
Write-Host "=== ChromaGLS test summary ===" -ForegroundColor Cyan
if ($SuccessfulTests.Count -gt 0) {
    Write-Host "Successful: $($SuccessfulTests -join ', ')" -ForegroundColor Green
}
if ($FailedTests.Count -gt 0) {
    Write-Host "Failed: $($FailedTests -join ', ')" -ForegroundColor Red
    exit 1
}

Write-Host "All configured game-version tests passed." -ForegroundColor Green
