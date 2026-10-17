#Requires -Version 7.0
param(
    [string]$UnityEditorPath = "C:\Program Files\Unity\Hub\Editor\6000.3.13f1\Editor\Unity.exe",
    [switch]$Force,
    [switch]$VerifyOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$ProjectPath = Join-Path $PSScriptRoot "Ferrofluid.Unity"
$OutputPath = Join-Path $PSScriptRoot "ChromaGLS/Settings/ferrofluid-windows.bundle"
$HashManifestPath = $OutputPath + ".sources.sha256"
$SourceInputs = @(
    [pscustomobject]@{ Key = "common-hlsl"; Path = (Join-Path $ProjectPath "Assets/FerrofluidCommon.cginc") },
    [pscustomobject]@{ Key = "simulation-shader"; Path = (Join-Path $ProjectPath "Assets/FerrofluidSimulation.shader") },
    [pscustomobject]@{ Key = "surface-shader"; Path = (Join-Path $ProjectPath "Assets/FerrofluidSurface.shader") },
    [pscustomobject]@{ Key = "bundle-builder"; Path = (Join-Path $ProjectPath "Assets/Editor/BuildFerrofluidBundle.cs") },
    [pscustomobject]@{ Key = "xr-setup"; Path = (Join-Path $ProjectPath "Assets/Editor/MockHmdProjectSetup.cs") },
    [pscustomobject]@{ Key = "packages"; Path = (Join-Path $ProjectPath "Packages/manifest.json") },
    [pscustomobject]@{ Key = "unity-version"; Path = (Join-Path $ProjectPath "ProjectSettings/ProjectVersion.txt") },
    [pscustomobject]@{ Key = "thin-film-lut"; Path = (Join-Path $ProjectPath "SourceTextures/FerroLUT.png") },
    [pscustomobject]@{ Key = "environment-map"; Path = (Join-Path $ProjectPath "SourceTextures/env-map-01.jpg") }
)

. (Join-Path $PSScriptRoot "AssertFerrofluidProjectDependencies.ps1")
Assert-FerrofluidXrPackages -ProjectPath $ProjectPath

$ExpectedHashes = @{}
if (Test-Path -LiteralPath $HashManifestPath -PathType Leaf)
{
    foreach ($Line in Get-Content -LiteralPath $HashManifestPath)
    {
        $Parts = $Line -split " ", 2
        if ($Parts.Count -eq 2)
        {
            $ExpectedHashes[$Parts[0]] = $Parts[1]
        }
    }
}

$CurrentHashes = @()
$MismatchReasons = @()
foreach ($SourceInput in $SourceInputs)
{
    if (-not (Test-Path -LiteralPath $SourceInput.Path -PathType Leaf))
    {
        throw "Required ferrofluid bundle source not found: $($SourceInput.Path)"
    }

    $Hash = (Get-FileHash -LiteralPath $SourceInput.Path -Algorithm SHA256).Hash
    $CurrentHashes += "$($SourceInput.Key) $Hash"
    if (-not $ExpectedHashes.ContainsKey($SourceInput.Key) -or $ExpectedHashes[$SourceInput.Key] -ne $Hash)
    {
        $MismatchReasons += "$($SourceInput.Key): source changed or has no recorded hash"
    }
}

if (-not (Test-Path -LiteralPath $OutputPath -PathType Leaf))
{
    $MismatchReasons += "bundle file is missing: $OutputPath"
}
elseif (-not $ExpectedHashes.ContainsKey("bundle") -or
    $ExpectedHashes["bundle"] -ne (Get-FileHash -LiteralPath $OutputPath -Algorithm SHA256).Hash)
{
    $MismatchReasons += "bundle changed or has no recorded hash"
}

if ($VerifyOnly)
{
    if ($MismatchReasons.Count -gt 0)
    {
        throw "Ferrofluid bundle needs rebuilding:`n$($MismatchReasons -join "`n")`nRun pwsh ./BuildFerrofluidBundle.ps1 with Unity installed."
    }

    Write-Host "Ferrofluid bundle verified: $OutputPath" -ForegroundColor Green
    return
}
if (-not $Force -and $MismatchReasons.Count -eq 0)
{
    Write-Host "Ferrofluid shader bundle is up to date: $OutputPath" -ForegroundColor Green
    return
}

if (-not (Test-Path -LiteralPath $UnityEditorPath -PathType Leaf))
{
    throw "Unity editor not found: $UnityEditorPath"
}

$LogDirectory = Join-Path $ProjectPath "Logs"
$LogPath = Join-Path $LogDirectory "ferrofluid-bundle-build.log"
New-Item -ItemType Directory -Path $LogDirectory -Force | Out-Null
# ArgumentList preserves paths containing spaces on both Windows and Linux, and WaitForExit waits for Unity's GUI executable too.
$StartInfo = [System.Diagnostics.ProcessStartInfo]::new()
$StartInfo.FileName = $UnityEditorPath
$StartInfo.UseShellExecute = $false
$StartInfo.CreateNoWindow = $true
foreach ($Argument in @(
    "-batchmode", "-quit", "-nographics",
    "-projectPath", $ProjectPath,
    "-buildTarget", "StandaloneWindows64",
    "-executeMethod", "ChromaGLS.FerrofluidBundle.BuildFerrofluidBundle.BuildWindows",
    "-logFile", $LogPath))
{
    $StartInfo.ArgumentList.Add($Argument)
}

$BuildStartedUtc = [DateTime]::UtcNow
$UnityProcess = [System.Diagnostics.Process]::Start($StartInfo)
try
{
    $UnityProcess.WaitForExit()
    if ($UnityProcess.ExitCode -ne 0)
    {
        Get-Content -LiteralPath $LogPath -Tail 120 -ErrorAction SilentlyContinue
        throw "Unity failed to build the ferrofluid shader bundle with exit code $($UnityProcess.ExitCode)."
    }
}
finally
{
    $UnityProcess.Dispose()
}

if (-not (Test-Path -LiteralPath $OutputPath -PathType Leaf) -or
    (Get-Item -LiteralPath $OutputPath).LastWriteTimeUtc -lt $BuildStartedUtc)
{
    Get-Content -LiteralPath $LogPath -Tail 120
    throw "Unity exited without producing a fresh ferrofluid shader bundle: $OutputPath"
}

# This stamp is a local incremental-build cache. CI builds from source and transfers only the bundle artifact.
$BundleHash = (Get-FileHash -LiteralPath $OutputPath -Algorithm SHA256).Hash
@($CurrentHashes; "bundle $BundleHash") | Set-Content -LiteralPath $HashManifestPath -Encoding utf8NoBOM
Write-Host "Built ferrofluid shader bundle: $OutputPath" -ForegroundColor Green
