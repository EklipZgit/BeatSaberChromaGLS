#requires -Version 7.0
# Local-only behavior checks for ResolveAssetBundleLoadingTools.ps1: fake zips and a stubbed
# Invoke-WebRequest, no network. -FixtureSdkPath must point at a real approved SDK DLL used as
# the download payload (for example the cached build-dependency copy).
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $FixtureSdkPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$script:resolver = Join-Path $repoRoot 'ResolveAssetBundleLoadingTools.ps1'
$script:work = Join-Path $repoRoot ('../Scratch/ferro-optional-reflections/phase5/test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $work | Out-Null

Add-Type -AssemblyName System.IO.Compression.FileSystem

function global:Invoke-WebRequest {
    param($Uri, $OutFile, [switch] $UseBasicParsing, $TimeoutSec)
    $global:downloadCalls++
    Copy-Item -LiteralPath $global:fixtureZip -Destination $OutFile
}

function New-FixtureZip {
    param([string] $PayloadPath)
    $staging = Join-Path $work ('zip-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force -Path (Join-Path $staging 'Plugins') | Out-Null
    Copy-Item -LiteralPath $PayloadPath -Destination (Join-Path $staging 'Plugins/AssetBundleLoadingTools.dll')
    $zip = Join-Path $work ('fixture-' + [guid]::NewGuid().ToString('N') + '.zip')
    [System.IO.Compression.ZipFile]::CreateFromDirectory($staging, $zip)
    Remove-Item -Recurse -Force $staging
    return $zip
}

function Get-DllMd5 {
    param([string] $Path)
    $md5 = [System.Security.Cryptography.MD5]::Create()
    try {
        $stream = [System.IO.File]::OpenRead($Path)
        try {
            return ([System.BitConverter]::ToString($md5.ComputeHash($stream))).Replace('-', '').ToLowerInvariant()
        }
        finally { $stream.Dispose() }
    }
    finally { $md5.Dispose() }
}

$results = [System.Collections.Generic.List[string]]::new()
$script:failed = $false
function Check {
    param([bool] $Condition, [string] $Name)
    $results.Add(('{0} {1}' -f ($(if ($Condition) { 'PASS' } else { 'FAIL' })), $Name))
    if (-not $Condition) { $script:failed = $true }
}

$goodDll = Join-Path $work 'good.dll'
Copy-Item -LiteralPath $FixtureSdkPath -Destination $goodDll
$goodMd5 = Get-DllMd5 $goodDll

try {
    # Cold download of a matching zip installs the pinned DLL.
    $cache = Join-Path $work 'cache1'
    $global:fixtureZip = New-FixtureZip -PayloadPath $goodDll
    $global:downloadCalls = 0
    & $script:resolver -Version '9.9.9' -DownloadUrl 'https://example.invalid/x.zip' -ExpectedDllMd5 $goodMd5 -CacheDirectory $cache | Out-Null
    Check (Test-Path -LiteralPath (Join-Path $cache '9.9.9/AssetBundleLoadingTools.dll') -PathType Leaf) 'cold download installs the pinned DLL'
    Check ($global:downloadCalls -eq 1) 'cold download issued exactly one request'

    # Warm cache matching the pin short-circuits with no download.
    $global:downloadCalls = 0
    & $script:resolver -Version '9.9.9' -DownloadUrl 'https://example.invalid/x.zip' -ExpectedDllMd5 $goodMd5 -CacheDirectory $cache | Out-Null
    Check ($global:downloadCalls -eq 0) 'matching cached DLL avoids any request'

    # Wrong-MD5 payload is rejected, not installed, and leaves no stray temps.
    $cache = Join-Path $work 'cache3'
    $bad = Join-Path $work 'bad.bin'
    [System.IO.File]::WriteAllBytes($bad, [byte[]](1, 2, 3, 4))
    $global:fixtureZip = New-FixtureZip -PayloadPath $bad
    $threw = $false
    try {
        & $script:resolver -Version '9.9.9' -DownloadUrl 'https://example.invalid/x.zip' -ExpectedDllMd5 $goodMd5 -CacheDirectory $cache 2>$null | Out-Null
    }
    catch { $threw = $true }
    Check $threw 'wrong-MD5 payload fails the resolver'
    Check (-not (Test-Path -LiteralPath (Join-Path $cache '9.9.9/AssetBundleLoadingTools.dll') -PathType Leaf)) 'rejected payload is not installed'
    Check (@(Get-ChildItem -LiteralPath $cache -Filter 'ablt-*' -File -ErrorAction SilentlyContinue).Count -eq 0) 'only the resolver owned temps are cleaned up'

    # Corrupt existing cache entry is reported, never silently deleted or overwritten.
    $cache = Join-Path $work 'cache4'
    New-Item -ItemType Directory -Force -Path (Join-Path $cache '9.9.9') | Out-Null
    $corrupt = Join-Path $cache '9.9.9/AssetBundleLoadingTools.dll'
    [System.IO.File]::WriteAllBytes($corrupt, [byte[]](9, 9))
    $global:downloadCalls = 0
    $threw = $false
    try {
        & $script:resolver -Version '9.9.9' -DownloadUrl 'https://example.invalid/x.zip' -ExpectedDllMd5 $goodMd5 -CacheDirectory $cache 2>$null | Out-Null
    }
    catch { $threw = $true }
    Check $threw 'corrupt existing cache entry fails loudly'
    Check ((Get-Item -LiteralPath $corrupt).Length -eq 2) 'corrupt file is left untouched for the user to inspect'
}
finally {
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
    Remove-Item function:Invoke-WebRequest -ErrorAction SilentlyContinue
}

$results | ForEach-Object { Write-Output $_ }
if ($script:failed) { exit 1 }
Write-Output 'ALL RESOLVER TESTS PASSED'
