#Requires -Version 7.0
# Renders the shipped ferrofluid torus under the Unity Mock HMD provider (Single Pass Instanced)
# and writes left/right eye captures to Ferrofluid.Unity/StereoTestCaptures/ for review.
#
# Two phases: the editor configures MockHMD and builds a standalone player (editor play mode in
# batchmode never runs the render pipeline), then the hidden player renders offscreen, captures both eyes,
# and exits with code 0 = both eyes rendered, 4 = a stereo pixel assertion failed.
param(
    [string]$UnityEditorPath = "C:\Program Files\Unity\Hub\Editor\6000.3.13f1\Editor\Unity.exe",
    # Renders the torus with the project-compiled FerrofluidSurface shader instead of the shipped
    # bundle, to distinguish a bundle variant-stripping issue from a shader-source issue.
    [switch]$UseSourceShader,
    # Phase 2 reuses the last built player; rebuild whenever the C# harness changes.
    [switch]$SkipBuild,
    # Negative control: omit the torus instance in the right eye while keeping the stereo color quad.
    [switch]$SimulateMissingRightEye,
    [int]$TimeoutSeconds = 720
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$ProjectPath = Join-Path $PSScriptRoot "Ferrofluid.Unity"
$LogDirectory = Join-Path $ProjectPath "Logs"
$EditorLogPath = Join-Path $LogDirectory "ferrofluid-stereo-build.log"
$PlayerLogPath = Join-Path $LogDirectory "ferrofluid-stereo-player.log"
$CaptureDir = Join-Path $ProjectPath "StereoTestCaptures"
$BundlePath = Join-Path $PSScriptRoot "ChromaGLS\Settings\ferrofluid-windows.bundle"
$PlayerExe = Join-Path $ProjectPath "Build\StereoTest\StereoTest.exe"
New-Item -ItemType Directory -Path $LogDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $CaptureDir -Force | Out-Null

# Check the committed XR dependencies before compiling the stereo test player.
. (Join-Path $PSScriptRoot "AssertFerrofluidProjectDependencies.ps1")
Assert-FerrofluidXrPackages -ProjectPath $ProjectPath

function Wait-ForProcess {
    param([System.Diagnostics.Process]$Process, [string]$Name, [string]$LogPath)
    $Exited = $Process.WaitForExit($TimeoutSeconds * 1000)
    if (-not $Exited) {
        $Process.Kill()
        Get-Content -LiteralPath $LogPath -Tail 80 -ErrorAction SilentlyContinue
        throw "$Name timed out after $TimeoutSeconds seconds."
    }
    return $Process.ExitCode
}

# Phase 1: batchmode editor configures MockHMD SPI + builds the stereo test player.
if (-not $SkipBuild) {
    $UnityArguments = @(
        "-batchmode",
        "-force-d3d11",
        "-projectPath", $ProjectPath,
        "-executeMethod", "ChromaGLS.StereoTest.Editor.FerrofluidStereoRunner.Run",
        "-logFile", $EditorLogPath
    )
    Write-Host "=== phase 1: building stereo test player ==="
    $UnityProcess = Start-Process -FilePath $UnityEditorPath -ArgumentList $UnityArguments -WindowStyle Hidden -PassThru
    $EditorExit = Wait-ForProcess -Process $UnityProcess -Name "Unity editor build" -LogPath $EditorLogPath
    Write-Host "editor build exited with code $EditorExit"
    if ($EditorExit -ne 0 -or -not (Test-Path -LiteralPath $PlayerExe)) {
        Write-Host "player build failed; tail of editor log:"
        Get-Content -LiteralPath $EditorLogPath -Tail 60 -ErrorAction SilentlyContinue
        exit $EditorExit
    }
} else {
    Write-Host "=== phase 1 skipped: reusing existing player build ==="
    if (-not (Test-Path -LiteralPath $PlayerExe)) {
        throw "No built player at $PlayerExe; run without -SkipBuild first."
    }
}

# Phase 2: the hidden player submits an offscreen stereo draw, reads both slices, and quits itself.
$PlayerArguments = @(
    "-force-d3d11",
    "-screen-width", "640",
    "-screen-height", "480",
    "-screen-fullscreen", "0",
    "-logFile", $PlayerLogPath,
    "-captureDir", $CaptureDir,
    "-bundlePath", $BundlePath
)
if ($UseSourceShader) {
    $PlayerArguments += "-useSourceShader"
}

if ($SimulateMissingRightEye)
{
    $PlayerArguments += "-simulateMissingRightEye"
}

Write-Host "=== phase 2: running stereo capture player ==="
$PlayerProcess = Start-Process -FilePath $PlayerExe -ArgumentList $PlayerArguments -WindowStyle Hidden -PassThru
$PlayerExit = Wait-ForProcess -Process $PlayerProcess -Name "stereo capture player" -LogPath $PlayerLogPath
Write-Host "player exited with code $PlayerExit"

Write-Host "--- captures ---"
Get-ChildItem -LiteralPath $CaptureDir -File | Sort-Object LastWriteTime -Descending | Select-Object -First 10 Name, Length, LastWriteTime | Format-Table | Out-Host
Write-Host "--- report ---"
$ReportPath = Join-Path $CaptureDir "report.txt"
if (Test-Path -LiteralPath $ReportPath) {
    Get-Content -LiteralPath $ReportPath | Out-Host
} else {
    Write-Host "no report.txt was written; tail of player log:"
    Get-Content -LiteralPath $PlayerLogPath -Tail 60 -ErrorAction SilentlyContinue
}
exit $PlayerExit
