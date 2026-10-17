#Requires -Version 7.0
param(
    [string]$UnityEditorPath = 'C:\Program Files\Unity\Hub\Editor\6000.3.13f1\Editor\Unity.exe',
    [switch]$Profile
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$projectPath = Join-Path $PSScriptRoot 'Ferrofluid.Unity'
$logPath = Join-Path $projectPath ('Logs/ferrofluid-' + ($Profile ? 'gpu-profile' : 'interaction-validation') + '.log')
New-Item -ItemType Directory -Force -Path (Split-Path $logPath) | Out-Null
$arguments = @(
    '-batchmode',
    '-force-d3d11',
    '-projectPath', ('"' + $projectPath + '"'),
    '-executeMethod', ('ChromaGLS.FerrofluidBundle.FerrofluidInteractionValidation.' + ($Profile ? 'Profile' : 'Run')),
    '-logFile', ('"' + $logPath + '"')
)
$validation = Start-Process -FilePath $UnityEditorPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
if (-not $validation.WaitForExit(300000))
{
    $validation.Kill()
    throw "Ferrofluid GPU validation timed out. Inspect $logPath."
}

Get-Content -LiteralPath $logPath | Select-String 'FerrofluidInteractionValidation|FerrofluidGpuProfile|Exception|error CS|Shader error'
exit $validation.ExitCode
