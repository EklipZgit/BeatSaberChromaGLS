#Requires -Version 7.0
param(
    [string]$UnityEditorPath,
    [switch]$Force,
    [switch]$VerifyOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$ProjectPath = Join-Path $PSScriptRoot "Ferrofluid.Unity"
$OutputPath = Join-Path $PSScriptRoot "ChromaGLS/Settings/ferrofluid-windows.bundle"
$ReflectionPackPath = Join-Path $PSScriptRoot "ChromaGLS/Settings/ferrofluid-reflections-windows.bundle"
$ReflectionManifestPath = Join-Path $PSScriptRoot "ChromaGLS/Settings/ferrofluid-reflections-pack.json"
$ManifestWriterPath = Join-Path $PSScriptRoot "WriteFerrofluidReflectionPackManifest.ps1"
$Outputs = @(
    [pscustomobject]@{ Key = "bundle"; Path = $OutputPath },
    [pscustomobject]@{ Key = "reflections-bundle"; Path = $ReflectionPackPath }
)
$HashManifestPath = $OutputPath + ".sources.sha256"
$SourceInputs = @(
    [pscustomobject]@{ Key = "common-hlsl"; Path = (Join-Path $ProjectPath "Assets/FerrofluidCommon.cginc") },
    [pscustomobject]@{ Key = "surface-correspondence-hlsl"; Path = (Join-Path $ProjectPath "Assets/FerrofluidSurfaceCorrespondence.cginc") },
    [pscustomobject]@{ Key = "simulation-shader"; Path = (Join-Path $ProjectPath "Assets/FerrofluidSimulation.shader") },
    [pscustomobject]@{ Key = "surface-shader"; Path = (Join-Path $ProjectPath "Assets/FerrofluidSurface.shader") },
    [pscustomobject]@{ Key = "floor-shadow-shader"; Path = (Join-Path $ProjectPath "Assets/FerrofluidFloorShadow.shader") },
    [pscustomobject]@{ Key = "parabolic-floor-reflection-shader"; Path = (Join-Path $ProjectPath "Assets/ParabolicFloorReflection.shader") },
    [pscustomobject]@{ Key = "menu-light-core-occlusion-shader"; Path = (Join-Path $ProjectPath "Assets/MenuLightCoreOcclusion.shader") },
    [pscustomobject]@{ Key = "menu-light-cube-spill-shader"; Path = (Join-Path $ProjectPath "Assets/MenuLightCubeSpill.shader") },
    [pscustomobject]@{ Key = "snapshot-readback-shader"; Path = (Join-Path $ProjectPath "Assets/FerrofluidSnapshotReadback.shader") },
    [pscustomobject]@{ Key = "bundle-launcher"; Path = $PSCommandPath },
    [pscustomobject]@{ Key = "bundle-builder"; Path = (Join-Path $ProjectPath "Assets/Editor/BuildFerrofluidBundle.cs") },
    [pscustomobject]@{ Key = "xr-setup"; Path = (Join-Path $ProjectPath "Assets/Editor/MockHmdProjectSetup.cs") },
    [pscustomobject]@{ Key = "packages"; Path = (Join-Path $ProjectPath "Packages/manifest.json") },
    [pscustomobject]@{ Key = "unity-version"; Path = (Join-Path $ProjectPath "ProjectSettings/ProjectVersion.txt") },
    [pscustomobject]@{ Key = "player-settings"; Path = (Join-Path $ProjectPath "ProjectSettings/ProjectSettings.asset") },
    [pscustomobject]@{ Key = "thin-film-lut"; Path = (Join-Path $ProjectPath "SourceTextures/FerroLUT.png") },
    [pscustomobject]@{ Key = "environment-map"; Path = (Join-Path $ProjectPath "SourceTextures/env-map-01.jpg") },
    [pscustomobject]@{ Key = "room-reflections-baker"; Path = (Join-Path $ProjectPath "Assets/Editor/FerrofluidRoomReflectionAssets.cs") },
    [pscustomobject]@{ Key = "room-reflections-manifest"; Path = (Join-Path $ProjectPath "SourceTextures/RoomReflections/manifest.json") },
    [pscustomobject]@{ Key = "floor-feet-capture"; Path = (Join-Path $ProjectPath "SourceTextures/RoomReflections/floor-feet.exr") },
    [pscustomobject]@{ Key = "room-reflection-on-0"; Path = (Join-Path $ProjectPath "SourceTextures/RoomReflections/on-0-positiveX.exr") },
    [pscustomobject]@{ Key = "room-reflection-on-1"; Path = (Join-Path $ProjectPath "SourceTextures/RoomReflections/on-1-negativeX.exr") },
    [pscustomobject]@{ Key = "room-reflection-on-2"; Path = (Join-Path $ProjectPath "SourceTextures/RoomReflections/on-2-positiveY.exr") },
    [pscustomobject]@{ Key = "room-reflection-on-3"; Path = (Join-Path $ProjectPath "SourceTextures/RoomReflections/on-3-negativeY.exr") },
    [pscustomobject]@{ Key = "room-reflection-on-4"; Path = (Join-Path $ProjectPath "SourceTextures/RoomReflections/on-4-positiveZ.exr") },
    [pscustomobject]@{ Key = "room-reflection-on-5"; Path = (Join-Path $ProjectPath "SourceTextures/RoomReflections/on-5-negativeZ.exr") },
    [pscustomobject]@{ Key = "room-reflection-off-0"; Path = (Join-Path $ProjectPath "SourceTextures/RoomReflections/off-0-positiveX.exr") },
    [pscustomobject]@{ Key = "room-reflection-off-1"; Path = (Join-Path $ProjectPath "SourceTextures/RoomReflections/off-1-negativeX.exr") },
    [pscustomobject]@{ Key = "room-reflection-off-2"; Path = (Join-Path $ProjectPath "SourceTextures/RoomReflections/off-2-positiveY.exr") },
    [pscustomobject]@{ Key = "room-reflection-off-3"; Path = (Join-Path $ProjectPath "SourceTextures/RoomReflections/off-3-negativeY.exr") },
    [pscustomobject]@{ Key = "room-reflection-off-4"; Path = (Join-Path $ProjectPath "SourceTextures/RoomReflections/off-4-positiveZ.exr") },
    [pscustomobject]@{ Key = "room-reflection-off-5"; Path = (Join-Path $ProjectPath "SourceTextures/RoomReflections/off-5-negativeZ.exr") },
    [pscustomobject]@{ Key = "room-reflections-high-manifest"; Path = (Join-Path $ProjectPath "SourceTextures/RoomReflectionsHigh/manifest.json") },
    [pscustomobject]@{ Key = "floor-feet-high-capture"; Path = (Join-Path $ProjectPath "SourceTextures/RoomReflectionsHigh/floor-feet.exr") },
    [pscustomobject]@{ Key = "room-reflection-high-on-0"; Path = (Join-Path $ProjectPath "SourceTextures/RoomReflectionsHigh/on-0-positiveX.exr") },
    [pscustomobject]@{ Key = "room-reflection-high-on-1"; Path = (Join-Path $ProjectPath "SourceTextures/RoomReflectionsHigh/on-1-negativeX.exr") },
    [pscustomobject]@{ Key = "room-reflection-high-on-2"; Path = (Join-Path $ProjectPath "SourceTextures/RoomReflectionsHigh/on-2-positiveY.exr") },
    [pscustomobject]@{ Key = "room-reflection-high-on-3"; Path = (Join-Path $ProjectPath "SourceTextures/RoomReflectionsHigh/on-3-negativeY.exr") },
    [pscustomobject]@{ Key = "room-reflection-high-on-4"; Path = (Join-Path $ProjectPath "SourceTextures/RoomReflectionsHigh/on-4-positiveZ.exr") },
    [pscustomobject]@{ Key = "room-reflection-high-on-5"; Path = (Join-Path $ProjectPath "SourceTextures/RoomReflectionsHigh/on-5-negativeZ.exr") },
    [pscustomobject]@{ Key = "room-reflection-high-off-0"; Path = (Join-Path $ProjectPath "SourceTextures/RoomReflectionsHigh/off-0-positiveX.exr") },
    [pscustomobject]@{ Key = "room-reflection-high-off-1"; Path = (Join-Path $ProjectPath "SourceTextures/RoomReflectionsHigh/off-1-negativeX.exr") },
    [pscustomobject]@{ Key = "room-reflection-high-off-2"; Path = (Join-Path $ProjectPath "SourceTextures/RoomReflectionsHigh/off-2-positiveY.exr") },
    [pscustomobject]@{ Key = "room-reflection-high-off-3"; Path = (Join-Path $ProjectPath "SourceTextures/RoomReflectionsHigh/off-3-negativeY.exr") },
    [pscustomobject]@{ Key = "room-reflection-high-off-4"; Path = (Join-Path $ProjectPath "SourceTextures/RoomReflectionsHigh/off-4-positiveZ.exr") },
    [pscustomobject]@{ Key = "room-reflection-high-off-5"; Path = (Join-Path $ProjectPath "SourceTextures/RoomReflectionsHigh/off-5-negativeZ.exr") }
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

$OutputHashes = @{}
foreach ($Output in $Outputs)
{
    if (-not (Test-Path -LiteralPath $Output.Path -PathType Leaf))
    {
        $MismatchReasons += "bundle file is missing: $($Output.Path)"
        continue
    }

    $OutputHashes[$Output.Key] = (Get-FileHash -LiteralPath $Output.Path -Algorithm SHA256).Hash
    if (-not $ExpectedHashes.ContainsKey($Output.Key) -or
        $ExpectedHashes[$Output.Key] -ne $OutputHashes[$Output.Key])
    {
        $MismatchReasons += "$($Output.Key): changed or has no recorded hash"
    }
}

if ($VerifyOnly)
{
    if (-not (Test-Path -LiteralPath $ReflectionManifestPath -PathType Leaf))
    {
        $MismatchReasons += "reflection pack descriptor is missing"
    }
    elseif ($OutputHashes.ContainsKey("reflections-bundle"))
    {
        $Descriptor = Get-Content -LiteralPath $ReflectionManifestPath -Raw | ConvertFrom-Json
        $ReflectionHash = $OutputHashes["reflections-bundle"].ToLowerInvariant()
        if ($Descriptor.schemaVersion -ne 1 -or
            $Descriptor.sha256 -ne $ReflectionHash -or
            $Descriptor.assetName -ne "ChromaGLS-Ferrofluid-Reflections-$ReflectionHash.bundle" -or
            $Descriptor.byteLength -ne (Get-Item -LiteralPath $ReflectionPackPath).Length)
        {
            $MismatchReasons += "reflection pack descriptor does not match the bundle"
        }
    }

    if ($MismatchReasons.Count -gt 0)
    {
        throw "Ferrofluid bundle needs rebuilding:`n$($MismatchReasons -join "`n")`nRun pwsh ./BuildFerrofluidBundle.ps1 with Unity installed."
    }

    Write-Host "Ferrofluid bundle verified: $OutputPath" -ForegroundColor Green
    return
}
if (-not $Force -and $MismatchReasons.Count -eq 0)
{
    & $ManifestWriterPath
    Write-Host "Ferrofluid shader bundle is up to date: $OutputPath" -ForegroundColor Green
    return
}

if ([string]::IsNullOrWhiteSpace($UnityEditorPath))
{
    # Follow the project upgrade instead of reopening it in an older editor and reimporting its assets.
    $VersionPath = Join-Path $ProjectPath "ProjectSettings/ProjectVersion.txt"
    $VersionLine = Get-Content -LiteralPath $VersionPath | Where-Object { $_ -match '^m_EditorVersion:' } | Select-Object -First 1
    if ($VersionLine -notmatch '^m_EditorVersion:\s*(\S+)\s*$')
    {
        throw "Cannot determine the Unity editor version from $VersionPath. Pass -UnityEditorPath explicitly."
    }

    $EditorVersion = $Matches[1]
    if ($IsWindows)
    {
        $EditorRoot = Join-Path $env:ProgramFiles "Unity/Hub/Editor"
        $EditorExecutable = "Editor/Unity.exe"
    }
    elseif ($IsMacOS)
    {
        $EditorRoot = "/Applications/Unity/Hub/Editor"
        $EditorExecutable = "Unity.app/Contents/MacOS/Unity"
    }
    else
    {
        $EditorRoot = Join-Path $HOME "Unity/Hub/Editor"
        $EditorExecutable = "Editor/Unity"
    }

    $UnityEditorPath = Join-Path (Join-Path $EditorRoot $EditorVersion) $EditorExecutable
}

if (-not (Test-Path -LiteralPath $UnityEditorPath -PathType Leaf))
{
    throw "Unity editor not found: $UnityEditorPath"
}

Write-Host "Building ferrofluid bundle with: $UnityEditorPath"
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

foreach ($Output in $Outputs)
{
    # Unity can reuse an unchanged reflection pack when only the core shaders changed.
    if (-not (Test-Path -LiteralPath $Output.Path -PathType Leaf) -or
        ($Output.Key -eq "bundle" -and (Get-Item -LiteralPath $Output.Path).LastWriteTimeUtc -lt $BuildStartedUtc))
    {
        Get-Content -LiteralPath $LogPath -Tail 120
        throw "Unity exited without producing a fresh ferrofluid bundle: $($Output.Path)"
    }
}

# This stamp is a local incremental-build cache. CI transfers both bundles separately.
$OutputHashLines = foreach ($Output in $Outputs)
{
    "$($Output.Key) $((Get-FileHash -LiteralPath $Output.Path -Algorithm SHA256).Hash)"
}
@($CurrentHashes; $OutputHashLines) | Set-Content -LiteralPath $HashManifestPath -Encoding utf8NoBOM
& $ManifestWriterPath
Write-Host "Built ferrofluid shader bundle: $OutputPath" -ForegroundColor Green
