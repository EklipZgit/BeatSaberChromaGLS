#Requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Run the real build dispatcher with inert build/deploy boundaries.
$fixtureDirectory = Join-Path $PSScriptRoot "../../Scratch/debug-build-testing/build-dispatch-$([Guid]::NewGuid().ToString('N'))"
$null = New-Item -ItemType Directory -Path $fixtureDirectory -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../BuildAllVersions.ps1') -Destination $fixtureDirectory
Set-Content -LiteralPath (Join-Path $fixtureDirectory 'ScriptCommon.ps1') -Value ''
Set-Content -LiteralPath (Join-Path $fixtureDirectory 'BuildFerrofluidBundle.ps1') -Value ''
Set-Content -LiteralPath (Join-Path $fixtureDirectory 'ChromaGLS.sln') -Value ''
Set-Content -LiteralPath (Join-Path $fixtureDirectory 'BeatSaberDevelopmentPlayer.ps1') -Value @'
#Requires -Version 7.0
function Set-BeatSaberDevelopmentPlayer
{
    param($GameDirectory, $UnityEditorPath, [switch]$CheckOnly)
    $global:BuildTestPlayerCalls.Add([bool]$CheckOnly)
    $global:BuildTestPlayerDirectories.Add($GameDirectory)
}
'@

function dotnet
{
    $global:BuildTestArguments = @($args)
    $configurationIndex = [Array]::IndexOf($global:BuildTestArguments, '-c')
    $global:BuildTestConfigurations.Add($global:BuildTestArguments[$configurationIndex + 1])
    $global:LASTEXITCODE = 0
}

$buildScript = Join-Path $fixtureDirectory 'BuildAllVersions.ps1'
$scenarios = @(
    @{ Arguments = @{}; Configuration = 'Debug'; Deploy = $true; Tweaks = $false },
    @{ Arguments = @{ Debug = $true }; Configuration = 'Debug'; Deploy = $true; Tweaks = $false },
    @{ Arguments = @{ Release = $true }; Configuration = 'Release'; Deploy = $false; Tweaks = $false },
    @{ Arguments = @{ FerrofluidTweaks = $true }; Configuration = 'Debug'; Deploy = $true; Tweaks = $true },
    @{ Arguments = @{ Release = $true; FerrofluidTweaks = $true }; Configuration = 'Debug'; Deploy = $true; Tweaks = $true },
    @{ Arguments = @{ Debug = $true; NoDeploy = $true }; Configuration = 'Debug'; Deploy = $false; Tweaks = $false }
)
foreach ($gameVersion in @('1.29.1', '1.34.2', '1.37.1', '1.40.8', '1.42.1', '1.44.1'))
{
    foreach ($scenario in $scenarios)
    {
        $global:BuildTestArguments = @()
        $global:BuildTestPlayerCalls = [Collections.Generic.List[bool]]::new()
        $global:BuildTestPlayerDirectories = [Collections.Generic.List[string]]::new()
        $global:BuildTestConfigurations = [Collections.Generic.List[string]]::new()
        $parameters = $scenario.Arguments
        & $buildScript -Version $gameVersion @parameters
        $configurationIndex = [Array]::IndexOf($global:BuildTestArguments, '-c')
        if ($configurationIndex -lt 0 -or $global:BuildTestArguments[$configurationIndex + 1] -ne "$($scenario.Configuration)-$gameVersion")
        {
            throw "Wrong configuration for $($parameters | ConvertTo-Json -Compress)."
        }

        if (($global:BuildTestArguments -contains '-p:FerrofluidTweaks=true') -ne $scenario.Tweaks)
        {
            throw 'The debug flag changed the ferrofluid tweak controls.'
        }

        if ($scenario.Deploy -and $gameVersion -eq '1.44.1')
        {
            if (($global:BuildTestPlayerCalls -join ',') -ne 'True,False')
            {
                throw 'Debug deployment must preflight and install its Development Player.'
            }
        }
        elseif ($global:BuildTestPlayerCalls.Count -ne 0)
        {
            throw "The $gameVersion build unexpectedly changed the native player."
        }
    }
}

$global:BuildTestPlayerCalls.Clear()
$global:BuildTestPlayerDirectories.Clear()
$global:BuildTestConfigurations.Clear()
& $buildScript -FerrofluidTweaks
if ($global:BuildTestConfigurations.Count -ne 6)
{
    throw 'The ferrofluid build did not retain the full default plugin matrix.'
}

if (($global:BuildTestPlayerCalls -join ',') -ne 'True,False' -or
    @($global:BuildTestPlayerDirectories | Where-Object { $_ -ne $env:BEATSABER_1_44_1 }).Count -ne 0)
{
    throw 'The default matrix must preflight and install only the 1.44.1 Development Player.'
}

$global:BuildTestArguments = @()
$rejected = $false
try
{
    & $buildScript -Version '1.44.1' -Debug -Release -NoDeploy
}
catch
{
    $rejected = $_.Exception.Message -like '*Choose either -Debug or -Release*'
}
if (-not $rejected -or $global:BuildTestArguments.Count -ne 0)
{
    throw 'Conflicting build flags were not rejected before building.'
}

Write-Host 'All CGLS build configuration tests passed.'
