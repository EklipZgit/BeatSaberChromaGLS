#Requires -Version 7.0
Set-StrictMode -Version Latest

function Get-UnityPlayerProductVersion
{
    param([Parameter(Mandatory)][string]$PlayerPath)

    return [Diagnostics.FileVersionInfo]::GetVersionInfo($PlayerPath).ProductVersion
}

function Get-UnityPlayerVersion
{
    param([Parameter(Mandatory)][string]$PlayerPath)

    $productVersion = Get-UnityPlayerProductVersion -PlayerPath $PlayerPath
    if ($productVersion -match '^(?<Version>\d+\.\d+\.\d+[abfp]\d+)\b')
    {
        return $Matches.Version
    }

    # Unity 2019's numeric DLL version omits f1, which the Hub installation name requires.
    $startupLog = Join-Path (Split-Path -Parent $PlayerPath) 'Logs/_latest.log'
    if (Test-Path -LiteralPath $startupLog -PathType Leaf)
    {
        $versionLine = Select-String -LiteralPath $startupLog -Pattern 'Running on Unity (?<Version>\d+\.\d+\.\d+[abfp]\d+)' |
            Select-Object -First 1
        if ($null -ne $versionLine)
        {
            $unityVersion = $versionLine.Matches[0].Groups['Version'].Value
            $numericPrefix = $unityVersion -replace '[abfp]\d+$', '.'
            if ($productVersion.StartsWith($numericPrefix, [StringComparison]::Ordinal))
            {
                return $unityVersion
            }
        }
    }

    throw "Could not resolve the exact Unity version from '$PlayerPath' ('$productVersion'). Run this installation once to write Logs/_latest.log."
}

function Assert-BeatSaberPlayerStopped
{
    param([Parameter(Mandatory)][string]$GameDirectory)

    $gameExecutable = Join-Path $GameDirectory 'Beat Saber.exe'
    foreach ($process in Get-Process -Name 'Beat Saber' -ErrorAction SilentlyContinue)
    {
        if ([string]::Equals($process.Path, $gameExecutable, [StringComparison]::OrdinalIgnoreCase))
        {
            throw "Close Beat Saber at '$GameDirectory' before changing its Unity player."
        }
    }
}

function Restore-BeatSaberPlayerFiles
{
    param(
        [Parameter(Mandatory)][string]$GameDirectory,
        [Parameter(Mandatory)][string]$BackupDirectory,
        [Parameter(Mandatory)][hashtable]$Manifest
    )

    # Validate every backup before overwriting any installed file.
    foreach ($file in $Manifest.Files)
    {
        if ($file.Existed)
        {
            $backupPath = Join-Path $BackupDirectory $file.Name
            if ((Get-FileHash -LiteralPath $backupPath -Algorithm SHA256).Hash -ne $file.Hash)
            {
                throw "Original player backup failed verification: $backupPath"
            }
        }
    }

    foreach ($file in $Manifest.Files)
    {
        $destination = Join-Path $GameDirectory $file.Name
        if ($file.Existed)
        {
            Copy-Item -LiteralPath (Join-Path $BackupDirectory $file.Name) -Destination $destination -Force
        }
        elseif (Test-Path -LiteralPath $destination)
        {
            Remove-Item -LiteralPath $destination -Force
        }
    }
}

function Set-BeatSaberDevelopmentPlayer
{
    param(
        [Parameter(Mandatory)][string]$GameDirectory,
        [string]$UnityEditorPath,
        [switch]$Restore,
        [switch]$CheckOnly
    )

    $ErrorActionPreference = 'Stop'
    $GameDirectory = (Resolve-Path -LiteralPath $GameDirectory).Path
    Assert-BeatSaberPlayerStopped -GameDirectory $GameDirectory
    $playerPath = Join-Path $GameDirectory 'UnityPlayer.dll'
    $unityVersion = Get-UnityPlayerVersion -PlayerPath $playerPath
    $backupDirectory = Join-Path $GameDirectory '.chromagls-original-player'
    $manifestPath = Join-Path $backupDirectory 'manifest.json'
    $manifest = $null
    if (Test-Path -LiteralPath $manifestPath)
    {
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json -AsHashtable
        if ($manifest.GameDirectory -ne $GameDirectory -or $manifest.UnityVersion -ne $unityVersion)
        {
            throw "Player backup belongs to a different installation or Unity version: $manifestPath"
        }
    }

    if ($Restore)
    {
        if ($null -ne $manifest)
        {
            Restore-BeatSaberPlayerFiles -GameDirectory $GameDirectory -BackupDirectory $backupDirectory -Manifest $manifest
            Write-Host "Original Unity player restored: $GameDirectory"
        }

        return
    }

    if ([string]::IsNullOrWhiteSpace($UnityEditorPath))
    {
        $hubRoot = if ([string]::IsNullOrWhiteSpace($env:UNITYHUB_EDITORS_FOLDER_LOCATION))
        {
            Join-Path $env:ProgramFiles 'Unity/Hub/Editor'
        }
        else
        {
            $env:UNITYHUB_EDITORS_FOLDER_LOCATION
        }
        $UnityEditorPath = Join-Path $hubRoot "$unityVersion/Editor/Unity.exe"
    }

    $variationsDirectory = Join-Path (Split-Path -Parent $UnityEditorPath) 'Data/PlaybackEngines/windowsstandalonesupport/Variations'
    $variationDirectory = Join-Path $variationsDirectory 'win64_player_development_mono'
    # Unity 2019's Windows Mono development player predates the "player" directory prefix.
    if (-not (Test-Path -LiteralPath $variationDirectory -PathType Container))
    {
        $variationDirectory = Join-Path $variationsDirectory 'win64_development_mono'
    }

    $files = @(
        @{ Source = 'WindowsPlayer.exe'; Destination = 'Beat Saber.exe' },
        @{ Source = 'UnityPlayer.dll'; Destination = 'UnityPlayer.dll' },
        @{ Source = 'UnityCrashHandler64.exe'; Destination = 'UnityCrashHandler64.exe' }
    )
    if (Test-Path -LiteralPath (Join-Path $variationDirectory 'WinPixEventRuntime.dll'))
    {
        $files += @{ Source = 'WinPixEventRuntime.dll'; Destination = 'WinPixEventRuntime.dll' }
    }

    foreach ($file in $files)
    {
        if (-not (Test-Path -LiteralPath (Join-Path $variationDirectory $file.Source) -PathType Leaf))
        {
            throw "Install Unity $unityVersion with Windows Mono build support, or pass -UnityEditorPath to its Unity.exe. Missing $($file.Source) in '$variationDirectory'."
        }
    }

    $sourceVersion = Get-UnityPlayerProductVersion -PlayerPath (Join-Path $variationDirectory 'UnityPlayer.dll')
    $installedVersion = Get-UnityPlayerProductVersion -PlayerPath $playerPath
    if ($sourceVersion -ne $installedVersion)
    {
        throw "Beat Saber requires Unity $unityVersion, but the development player is $sourceVersion."
    }

    if ($CheckOnly)
    {
        return
    }

    if ($null -eq $manifest)
    {
        if (Test-Path -LiteralPath $backupDirectory)
        {
            throw "Preserve the incomplete player backup before retrying: $backupDirectory"
        }

        $null = New-Item -ItemType Directory -Path $backupDirectory
        $originalFiles = foreach ($file in $files)
        {
            $originalPath = Join-Path $GameDirectory $file.Destination
            $existed = Test-Path -LiteralPath $originalPath -PathType Leaf
            $originalHash = $null
            if ($existed)
            {
                Copy-Item -LiteralPath $originalPath -Destination (Join-Path $backupDirectory $file.Destination)
                $originalHash = (Get-FileHash -LiteralPath $originalPath -Algorithm SHA256).Hash
            }

            @{ Name = $file.Destination; Existed = $existed; Hash = $originalHash }
        }
        $manifest = @{ GameDirectory = $GameDirectory; UnityVersion = $unityVersion; Files = @($originalFiles) }
        $manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifestPath
    }

    try
    {
        foreach ($file in $files)
        {
            Copy-Item -LiteralPath (Join-Path $variationDirectory $file.Source) -Destination (Join-Path $GameDirectory $file.Destination) -Force
        }
    }
    catch
    {
        Restore-BeatSaberPlayerFiles -GameDirectory $GameDirectory -BackupDirectory $backupDirectory -Manifest $manifest
        throw
    }

    Write-Host "Unity $unityVersion Development Player installed: $GameDirectory"
    Write-Host "Original player backup: $backupDirectory"
}
