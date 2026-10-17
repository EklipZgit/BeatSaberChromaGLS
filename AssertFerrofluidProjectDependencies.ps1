#Requires -Version 7.0
# Shared setup for the Ferrofluid.Unity project, dot-sourced by BuildFerrofluidBundle.ps1 and
# TestFerrofluidStereo.ps1.

# Keep the XR packages in the committed manifest. The bundle builder configures MockHMD so Unity
# retains the stereo shader variants Beat Saber needs to render ferrofluid in both eyes.
function Assert-FerrofluidXrPackages
{
    param([Parameter(Mandatory)] [string]$ProjectPath)

    $ManifestPath = Join-Path $ProjectPath "Packages/manifest.json"
    $RequiredPackages = [ordered]@{
        "com.unity.xr.management" = "4.5.3"
        "com.unity.xr.mock-hmd"   = "1.5.0-exp.3"
    }
    if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf))
    {
        throw "Unity package manifest not found: $ManifestPath"
    }

    $Manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
    foreach ($Package in $RequiredPackages.GetEnumerator())
    {
        if ($Manifest.dependencies.PSObject.Properties.Name -notcontains $Package.Key -or
            $Manifest.dependencies.($Package.Key) -ne $Package.Value)
        {
            throw "Set $($Package.Key) to $($Package.Value) in $ManifestPath so the bundle retains its stereo shader variants."
        }
    }
}
