#Requires -Version 7.0

function Remove-StaleBsipaPendingPlugin {
    param(
        [Parameter(Mandatory)]
        [string]$GameDirectory,

        [Parameter(Mandatory)]
        [string]$PluginFileName
    )

    $livePath = Join-Path $GameDirectory "Plugins\$PluginFileName"
    $pendingPath = Join-Path $GameDirectory "IPA\Pending\Plugins\$PluginFileName"
    if (-not (Test-Path -LiteralPath $livePath -PathType Leaf) -or
        -not (Test-Path -LiteralPath $pendingPath -PathType Leaf)) {
        return
    }

    $liveFile = Get-Item -LiteralPath $livePath
    $pendingFile = Get-Item -LiteralPath $pendingPath

    # A later direct deployment must win over an older queued BSIPA deployment on the next game launch.
    if ($pendingFile.LastWriteTimeUtc -lt $liveFile.LastWriteTimeUtc) {
        Remove-Item -LiteralPath $pendingFile.FullName -Force
        Write-Host "Removed stale BSIPA pending plugin: $($pendingFile.FullName)" -ForegroundColor Yellow
        Write-Host "  Pending UTC: $($pendingFile.LastWriteTimeUtc.ToString('O'))" -ForegroundColor DarkYellow
        Write-Host "  Live UTC   : $($liveFile.LastWriteTimeUtc.ToString('O'))" -ForegroundColor DarkYellow
    }
}
