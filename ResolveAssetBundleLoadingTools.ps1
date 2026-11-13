#requires -Version 7.0
# Resolves the pinned AssetBundleLoadingTools compile-time reference for this game target.
# Downloads the approved BeatMods package once into the user cache; the runtime dependency
# itself is still installed by the mod manager and this script never touches the game install.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Version,

    [Parameter(Mandatory = $true)]
    [string] $DownloadUrl,

    [Parameter(Mandatory = $true)]
    [string] $ExpectedDllMd5,

    [Parameter(Mandatory = $true)]
    [string] $CacheDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.IO.Compression.FileSystem

$expectedMd5 = $ExpectedDllMd5.Trim().ToLowerInvariant().PadLeft(32, '0')
$versionDir = Join-Path $CacheDirectory $Version
$targetDll = Join-Path $versionDir 'AssetBundleLoadingTools.dll'

function Get-DllMd5 {
    param([string] $Path)
    $stream = [System.IO.File]::OpenRead($Path)
    try {
        $md5 = [System.Security.Cryptography.MD5]::Create()
        try {
            return ([System.BitConverter]::ToString($md5.ComputeHash($stream))).Replace('-', '').ToLowerInvariant()
        }
        finally {
            $md5.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Test-PinnedDll {
    param([string] $Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $false
    }
    return (Get-DllMd5 -Path $Path) -eq $expectedMd5
}

if (Test-Path -LiteralPath $targetDll -PathType Leaf) {
    if (Test-PinnedDll -Path $targetDll) {
        Write-Output "AssetBundleLoadingTools $Version already cached at $targetDll"
        exit 0
    }
    throw "Cached AssetBundleLoadingTools at '$targetDll' does not match the approved MD5 $expectedMd5 for version $Version. The cache entry is corrupt or foreign; rename it aside and build again."
}

New-Item -ItemType Directory -Force -Path $versionDir | Out-Null

$tempZip = Join-Path $CacheDirectory ("ablt-$([guid]::NewGuid().ToString('N')).zip")
$tempDll = Join-Path $CacheDirectory ("ablt-$([guid]::NewGuid().ToString('N')).dll")
try {
    try {
        Invoke-WebRequest -Uri $DownloadUrl -OutFile $tempZip -UseBasicParsing -TimeoutSec 120
    }
    catch {
        throw "Could not download the approved AssetBundleLoadingTools $Version package from $DownloadUrl : $_"
    }

    $archive = [System.IO.Compression.ZipFile]::OpenRead($tempZip)
    try {
        $entry = $archive.GetEntry('Plugins/AssetBundleLoadingTools.dll')
        if ($null -eq $entry) {
            throw "The downloaded package does not contain 'Plugins/AssetBundleLoadingTools.dll'; the pinned SDK package layout is unexpected."
        }
        $entryStream = $entry.Open()
        try {
            $outStream = [System.IO.File]::Create($tempDll)
            try {
                $entryStream.CopyTo($outStream)
            }
            finally {
                $outStream.Dispose()
            }
        }
        finally {
            $entryStream.Dispose()
        }
    }
    finally {
        $archive.Dispose()
    }

    $actualMd5 = Get-DllMd5 -Path $tempDll
    if ($actualMd5 -ne $expectedMd5) {
        throw "Extracted AssetBundleLoadingTools.dll MD5 $actualMd5 does not match the approved pin $expectedMd5 for version $Version; refusing to install."
    }

    try {
        $assemblyName = [System.Reflection.AssemblyName]::GetAssemblyName($tempDll)
        if ($assemblyName.Name -ne 'AssetBundleLoadingTools') {
            throw "Extracted assembly name '$($assemblyName.Name)' is not 'AssetBundleLoadingTools'; refusing to install."
        }
    }
    catch [System.BadImageFormatException] {
        throw "Extracted file is not a valid managed assembly; refusing to install."
    }

    # Another parallel MSBuild target may have installed the pinned DLL while we downloaded.
    if (Test-Path -LiteralPath $targetDll -PathType Leaf) {
        if (Test-PinnedDll -Path $targetDll) {
            Write-Output "AssetBundleLoadingTools $Version resolved at $targetDll"
            exit 0
        }
        throw "A different AssetBundleLoadingTools appeared at '$targetDll' during download and does not match the approved pin; not overwriting an unknown file."
    }

    [System.IO.File]::Move($tempDll, $targetDll)
    Write-Output "AssetBundleLoadingTools $Version installed to $targetDll"
}
finally {
    foreach ($temp in @($tempZip, $tempDll)) {
        if (Test-Path -LiteralPath $temp -PathType Leaf) {
            try {
                [System.IO.File]::Delete($temp)
            }
            catch {
                Write-Warning "Could not delete temporary file '$temp': $_"
            }
        }
    }
}
