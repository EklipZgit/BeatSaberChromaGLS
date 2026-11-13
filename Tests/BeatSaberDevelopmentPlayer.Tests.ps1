#Requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../BeatSaberDevelopmentPlayer.ps1')

$fixtureDirectory = Join-Path $PSScriptRoot "../../Scratch/debug-build-testing/player-swap-$([Guid]::NewGuid().ToString('N'))"
$gameDirectory = Join-Path $fixtureDirectory 'Game'
$editorDirectory = Join-Path $fixtureDirectory 'Editor'
$variationDirectory = Join-Path $editorDirectory 'Data/PlaybackEngines/windowsstandalonesupport/Variations/win64_player_development_mono'
$null = New-Item -ItemType Directory -Path $gameDirectory, $variationDirectory -Force
foreach ($name in 'Beat Saber.exe', 'UnityPlayer.dll', 'UnityCrashHandler64.exe')
{
    Set-Content -LiteralPath (Join-Path $gameDirectory $name) -Value "original $name"
}

foreach ($name in 'WindowsPlayer.exe', 'UnityPlayer.dll', 'UnityCrashHandler64.exe', 'WinPixEventRuntime.dll')
{
    Set-Content -LiteralPath (Join-Path $variationDirectory $name) -Value "development $name"
}

$originalHashes = @{}
foreach ($file in Get-ChildItem -LiteralPath $gameDirectory -File)
{
    $originalHashes[$file.Name] = (Get-FileHash -LiteralPath $file.FullName).Hash
}

# Fixture payloads replace native PE files, while real filesystem swaps and hashes are exercised.
function Get-UnityPlayerProductVersion
{
    param($PlayerPath)
    if ($script:WrongVersion -and $PlayerPath -like '*Variations*')
    {
        return '6000.0.41f1'
    }

    return $script:PlayerProductVersion
}
function Assert-BeatSaberPlayerStopped
{
    param($GameDirectory)
    if ($script:Running)
    {
        throw 'Close Beat Saber before changing its Unity player.'
    }
}
$script:WrongVersion = $false
$script:Running = $false
$script:PlayerProductVersion = '2019.4.28.1278358'
$logDirectory = Join-Path $gameDirectory 'Logs'
$null = New-Item -ItemType Directory -Path $logDirectory
Set-Content -LiteralPath (Join-Path $logDirectory '_latest.log') -Value '[INFO | IPA] Running on Unity 2019.4.28f1'
if ((Get-UnityPlayerVersion -PlayerPath (Join-Path $gameDirectory 'UnityPlayer.dll')) -ne '2019.4.28f1')
{
    throw 'The legacy numeric DLL version did not resolve through the matching startup log.'
}

$script:PlayerProductVersion = '6000.0.40f1 (157d81624ddf)'
$editorPath = Join-Path $editorDirectory 'Unity.exe'

# Unity 2019 installs the same player files under a directory without "player" in its name.
$legacyVariationDirectory = Join-Path (Split-Path -Parent $variationDirectory) 'win64_development_mono'
Move-Item -LiteralPath $variationDirectory -Destination $legacyVariationDirectory
try
{
    Set-BeatSaberDevelopmentPlayer -GameDirectory $gameDirectory -UnityEditorPath $editorPath -CheckOnly
}
finally
{
    Move-Item -LiteralPath $legacyVariationDirectory -Destination $variationDirectory
}

Set-BeatSaberDevelopmentPlayer -GameDirectory $gameDirectory -UnityEditorPath $editorPath -CheckOnly
if (Test-Path -LiteralPath (Join-Path $gameDirectory '.chromagls-original-player'))
{
    throw 'Preflight wrote a backup.'
}

Set-BeatSaberDevelopmentPlayer -GameDirectory $gameDirectory -UnityEditorPath $editorPath
if ((Get-Content -LiteralPath (Join-Path $gameDirectory 'Beat Saber.exe') -Raw).Trim() -ne 'development WindowsPlayer.exe')
{
    throw 'The development executable was not installed under the game name.'
}

Set-BeatSaberDevelopmentPlayer -GameDirectory $gameDirectory -UnityEditorPath $editorPath
Set-BeatSaberDevelopmentPlayer -GameDirectory $gameDirectory -Restore
foreach ($name in $originalHashes.Keys)
{
    if ((Get-FileHash -LiteralPath (Join-Path $gameDirectory $name)).Hash -ne $originalHashes[$name])
    {
        throw "Restoration did not preserve $name after repeated installation."
    }
}
if (Test-Path -LiteralPath (Join-Path $gameDirectory 'WinPixEventRuntime.dll'))
{
    throw 'Restore left a development-only WinPix runtime.'
}

foreach ($failure in 'WrongVersion', 'Running', 'MissingFile')
{
    $script:WrongVersion = $failure -eq 'WrongVersion'
    $script:Running = $failure -eq 'Running'
    if ($failure -eq 'MissingFile')
    {
        Remove-Item -LiteralPath (Join-Path $variationDirectory 'WindowsPlayer.exe')
    }

    $rejected = $false
    try
    {
        Set-BeatSaberDevelopmentPlayer -GameDirectory $gameDirectory -UnityEditorPath $editorPath
    }
    catch
    {
        $expectedMessage = switch ($failure)
        {
            'WrongVersion' { '*requires Unity 6000.0.40f1*' }
            'Running' { '*Close Beat Saber*' }
            'MissingFile' { '*Missing WindowsPlayer.exe*' }
        }
        $rejected = $_.Exception.Message -like $expectedMessage
    }
    if (-not $rejected)
    {
        throw "$failure was not rejected for the expected reason."
    }

    foreach ($name in $originalHashes.Keys)
    {
        if ((Get-FileHash -LiteralPath (Join-Path $gameDirectory $name)).Hash -ne $originalHashes[$name])
        {
            throw "$failure modified $name."
        }
    }
}

$script:WrongVersion = $false
$script:Running = $false
Set-Content -LiteralPath (Join-Path $variationDirectory 'WindowsPlayer.exe') -Value 'development WindowsPlayer.exe'
function Copy-Item
{
    param($LiteralPath, $Destination, [switch]$Force)
    if ($LiteralPath -eq (Join-Path $variationDirectory 'UnityPlayer.dll'))
    {
        throw 'Simulated player copy failure.'
    }

    Microsoft.PowerShell.Management\Copy-Item -LiteralPath $LiteralPath -Destination $Destination -Force:$Force
}
$rejected = $false
try
{
    Set-BeatSaberDevelopmentPlayer -GameDirectory $gameDirectory -UnityEditorPath $editorPath
}
catch
{
    $rejected = $_.Exception.Message -eq 'Simulated player copy failure.'
}
if (-not $rejected)
{
    throw 'The partial-copy failure was not reported.'
}

foreach ($name in $originalHashes.Keys)
{
    if ((Get-FileHash -LiteralPath (Join-Path $gameDirectory $name)).Hash -ne $originalHashes[$name])
    {
        throw "Partial installation did not roll back $name."
    }
}

Write-Host 'All Development Player installation and restoration tests passed.'
