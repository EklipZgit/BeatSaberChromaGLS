#Requires -Version 5.1
<#
.SYNOPSIS
Builds BeatMods-ready, per-game release archives for a SemVer plugin release.

.DESCRIPTION
Each archive contains exactly Plugins/ChromaGLS.dll. BSIPA embeds the generated
manifest in that assembly, including the matching plugin version, game version,
and CustomJSONData/SongCore dependencies. BeatMods receives one archive for each game
version selected below. The archives are written to dist/ as
ChromaGLS-<version>-bs<game-version>.zip.

Use a new final SemVer version for every BeatMods upload. Do not add dependencies,
PDBs, or any other DLLs to the generated archives.

.EXAMPLE
    .\package-release.ps1 -Version 1.0.0

Builds an archive for every supported game version.

.EXAMPLE
    .\package-release.ps1 -Version 1.0.0 -GameVersion 1.42.1

Builds only the Beat Saber 1.42.1 archive.
#>
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$')]
    [string]$Version,

    [ValidateSet('1.29.1', '1.34.2', '1.37.1', '1.40.8', '1.44.1', '1.44.2')]
    [string[]]$GameVersion = @('1.29.1', '1.34.2', '1.37.1', '1.40.8', '1.44.1', '1.44.2')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$buildScript = Join-Path $PSScriptRoot 'build-all-versions.ps1'
$projectDir = Join-Path $PSScriptRoot 'ChromaGLS'
$distDir = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Force -Path $distDir | Out-Null

foreach ($game in $GameVersion) {
    & $buildScript -Version $game -Configuration Release -PluginVersion $Version

    $zipDir = Join-Path $projectDir "bin\\Release-$game\\net48\\zip"
    $sourceZip = Get-ChildItem -Path $zipDir -Filter '*.zip' -File |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
    if ($null -eq $sourceZip) {
        throw "No release archive was produced for Beat Saber $game."
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($sourceZip.FullName)
    try {
        if ($archive.Entries.Count -ne 1 -or $archive.Entries[0].FullName -ne 'Plugins/ChromaGLS.dll') {
            throw "Unexpected archive contents in $($sourceZip.FullName)."
        }
    }
    finally {
        $archive.Dispose()
    }

    $destination = Join-Path $distDir "ChromaGLS-$Version-bs$game.zip"
    Copy-Item -LiteralPath $sourceZip.FullName -Destination $destination -Force
    Write-Host "BeatMods archive ready: $destination" -ForegroundColor Green
}
