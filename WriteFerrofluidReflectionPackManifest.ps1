#Requires -Version 7.0
param(
    [string]$ReleaseTag = "",
    [string]$BundlePath = (Join-Path $PSScriptRoot "ChromaGLS/Settings/ferrofluid-reflections-windows.bundle"),
    [string]$OutputPath = (Join-Path $PSScriptRoot "ChromaGLS/Settings/ferrofluid-reflections-pack.json")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$Bundle = Get-Item -LiteralPath $BundlePath
$Hash = (Get-FileHash -LiteralPath $Bundle.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
$AssetName = "ChromaGLS-Ferrofluid-Reflections-$Hash.bundle"
$DownloadUrl = ""
if (-not [string]::IsNullOrWhiteSpace($ReleaseTag))
{
    $EncodedTag = [Uri]::EscapeDataString($ReleaseTag)
    $DownloadUrl = "https://github.com/EklipZgit/BeatSaberChromaGLS/releases/download/$EncodedTag/$AssetName"
}

# Local builds have no published asset URL. They can load the matching file manually,
# but must never download a different release's pack through a moving "latest" URL.
$Manifest = [ordered]@{
    schemaVersion = 1
    assetName = $AssetName
    sha256 = $Hash
    byteLength = $Bundle.Length
    downloadUrl = $DownloadUrl
}
$Manifest | ConvertTo-Json | Set-Content -LiteralPath $OutputPath -Encoding utf8NoBOM
Write-Host "Reflection pack descriptor: $OutputPath"
