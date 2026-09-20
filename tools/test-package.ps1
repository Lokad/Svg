param(
    [Parameter(Mandatory = $true)]
    [string] $PackageDirectory,
    [Parameter(Mandatory = $true)]
    [string] $ResultsDirectory
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $PackageDirectory)) { throw "Package directory does not exist: $PackageDirectory" }
$PackageDirectory = (Resolve-Path -LiteralPath $PackageDirectory).Path
$ResultsDirectory = [IO.Path]::GetFullPath($ResultsDirectory)
$packages = @(Get-ChildItem -LiteralPath $PackageDirectory -Filter 'Lokad.Svg.*.nupkg' -File)
if ($packages.Count -ne 1) { throw "Expected exactly one Lokad.Svg package in $PackageDirectory; found $($packages.Count)." }
$package = $packages[0]
$symbolPackages = @(Get-ChildItem -LiteralPath $PackageDirectory -Filter 'Lokad.Svg.*.snupkg' -File)
if ($symbolPackages.Count -ne 1) { throw "Expected exactly one Lokad.Svg symbol package in $PackageDirectory; found $($symbolPackages.Count)." }
$symbolPackage = $symbolPackages[0]
if ($symbolPackage.BaseName -ne $package.BaseName) { throw "Symbol package $($symbolPackage.Name) does not match runtime package $($package.Name)." }

Add-Type -AssemblyName System.IO.Compression
$archive = [IO.Compression.ZipFile]::OpenRead($package.FullName)
try {
    $entries = @($archive.Entries | ForEach-Object FullName)
    foreach ($required in @('LICENSE.txt', 'README.md', 'lib/net10.0/Lokad.Svg.dll', 'lib/net10.0/Lokad.Svg.xml'))
    {
        if ($entries -notcontains $required) { throw "Package is missing required entry: $required" }
    }
    if ($entries | Where-Object { $_.StartsWith('runtimes/', [StringComparison]::OrdinalIgnoreCase) }) { throw 'Package must not contain runtime-specific assets.' }
    $nuspecEntry = $archive.Entries | Where-Object { $_.FullName.EndsWith('.nuspec', [StringComparison]::OrdinalIgnoreCase) }
    if ($null -eq $nuspecEntry) { throw 'Package has no nuspec.' }
    $reader = [IO.StreamReader]::new($nuspecEntry.Open())
    try { [xml]$nuspec = $reader.ReadToEnd() }
    finally { $reader.Dispose() }
    $dependencyNodes = $nuspec.SelectNodes("//*[local-name()='dependency']")
    if ($dependencyNodes.Count -ne 0) { throw 'Package must not declare runtime package dependencies.' }
    $version = $nuspec.SelectSingleNode("//*[local-name()='version']")?.InnerText
    if ([string]::IsNullOrWhiteSpace($version)) { throw 'Package nuspec has no version.' }
}
finally { $archive.Dispose() }

$symbolArchive = [IO.Compression.ZipFile]::OpenRead($symbolPackage.FullName)
try {
    $symbolEntries = @($symbolArchive.Entries | ForEach-Object FullName)
    if ($symbolEntries -notcontains 'lib/net10.0/Lokad.Svg.pdb') { throw 'Symbol package is missing the net10.0 Lokad.Svg PDB.' }
    if ($symbolEntries | Where-Object { $_.StartsWith('runtimes/', [StringComparison]::OrdinalIgnoreCase) }) { throw 'Symbol package must not contain runtime-specific assets.' }
}
finally { $symbolArchive.Dispose() }

New-Item -ItemType Directory -Force -Path $ResultsDirectory | Out-Null
$consumer = Join-Path $ResultsDirectory ('package-consumer-' + [Guid]::NewGuid().ToString('N'))
$template = Join-Path $PSScriptRoot 'Lokad.Svg.PackageSmoke'
Copy-Item -LiteralPath $template -Destination $consumer -Recurse
$consumerProject = Join-Path $consumer 'Lokad.Svg.PackageSmoke.csproj'
$packageCache = Join-Path $consumer 'nuget-packages'
$previousPackageCache = $env:NUGET_PACKAGES
$env:NUGET_PACKAGES = $packageCache

try {
    & dotnet restore $consumerProject --source $PackageDirectory --ignore-failed-sources -p:RestoreNoCache=true "-p:LokadSvgPackageVersion=$version"
    if ($LASTEXITCODE -ne 0) { throw 'Could not restore the clean installed-package consumer.' }
    & dotnet build $consumerProject -c Release --no-restore "-p:LokadSvgPackageVersion=$version"
    if ($LASTEXITCODE -ne 0) { throw 'Could not build the clean installed-package consumer.' }
    & dotnet run --project $consumerProject -c Release --no-build "-p:LokadSvgPackageVersion=$version"
    if ($LASTEXITCODE -ne 0) { throw 'The clean installed-package consumer failed.' }
}
finally { $env:NUGET_PACKAGES = $previousPackageCache }

Write-Output "Package content and consumer scenarios passed. Consumer retained in $consumer."
