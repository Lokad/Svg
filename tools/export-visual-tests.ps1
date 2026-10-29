param(
    [Parameter(Mandatory = $true)]
    [string] $DestinationDirectory,
    [ValidateSet('net11.0', 'net10.0')]
    [string] $TargetFramework = 'net11.0'
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$destination = [IO.Path]::GetFullPath($DestinationDirectory)
$testOutput = Join-Path $repository "tests/Lokad.Svg.VisualTests/bin/Release/$TargetFramework"
$fontProbe = Join-Path $PSScriptRoot "Lokad.Svg.FontProbe/bin/Release/$TargetFramework"
$research = Join-Path $repository 'tests/Research'

if (Test-Path -LiteralPath $destination) { throw 'DestinationDirectory must be a new directory.' }
foreach ($required in @((Join-Path $testOutput 'Lokad.Svg.VisualTests.dll'), (Join-Path $fontProbe 'Lokad.Svg.FontProbe.dll'), (Join-Path $research 'manifest.json'))) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Build Release $TargetFramework before export. Missing: $required" }
}

New-Item -ItemType Directory -Path $destination | Out-Null
foreach ($item in Get-ChildItem -LiteralPath $testOutput -Force) {
    # Old ordinary outputs can still contain an obsolete corpus copy.
    if ($item.Name -ne 'Research') { Copy-Item -LiteralPath $item.FullName -Destination $destination -Recurse }
}
Copy-Item -LiteralPath $research -Destination (Join-Path $destination 'Research') -Recurse
Copy-Item -LiteralPath $fontProbe -Destination (Join-Path $destination 'FontProbe') -Recurse

$sourceCommit = (& git -C $repository rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Could not identify the source commit.' }
$researchTree = (& git -C $repository rev-parse 'HEAD:tests/Research').Trim()
if ($LASTEXITCODE -ne 0) { throw 'Could not identify the research tree.' }
$sourceModified = @(& git -C $repository status --porcelain --untracked-files=no).Count -ne 0
if ($LASTEXITCODE -ne 0) { throw 'Could not inspect the source state.' }
[ordered]@{
    schema = 1
    targetFramework = $TargetFramework
    sourceCommit = $sourceCommit
    sourceModified = $sourceModified
    committedResearchTree = $researchTree
    corpus = 'Research'
    testAssembly = 'Lokad.Svg.VisualTests.dll'
    fontHost = 'FontProbe/Lokad.Svg.FontProbe.dll'
    testAssemblySha256 = (Get-FileHash -LiteralPath (Join-Path $destination 'Lokad.Svg.VisualTests.dll') -Algorithm SHA256).Hash.ToLowerInvariant()
    librarySha256 = (Get-FileHash -LiteralPath (Join-Path $destination 'Lokad.Svg.dll') -Algorithm SHA256).Hash.ToLowerInvariant()
    fontHostSha256 = (Get-FileHash -LiteralPath (Join-Path $destination 'FontProbe/Lokad.Svg.FontProbe.dll') -Algorithm SHA256).Hash.ToLowerInvariant()
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $destination 'test-bundle.json') -Encoding utf8NoBOM

@'
This directory contains the offline visual suite, its independent references,
font licenses and the managed FontProbe child host. Install the matching runtime
and a .NET SDK with VSTest, then run from this directory:

dotnet vstest Lokad.Svg.VisualTests.dll

Outside a checkout the suite uses the bundled Research directory. To force the
bundle when running inside a checkout, set LOKAD_SVG_RESEARCH_ROOT to its absolute
Research path. Clear that variable to resume normal checkout-based tests.
'@ | Set-Content -LiteralPath (Join-Path $destination 'BUNDLE-README.txt') -Encoding utf8NoBOM

Write-Output "Exported $TargetFramework visual suite to $destination"
