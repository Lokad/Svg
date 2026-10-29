param(
    [Parameter(Mandatory = $true)]
    [string] $PackageDirectory,
    [Parameter(Mandatory = $true)]
    [string] $ResultsDirectory
)

$ErrorActionPreference = 'Stop'
$targetFrameworks = @('net11.0', 'net10.0')

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
    $requiredEntries = @('LICENSE.txt', 'THIRD_PARTY_NOTICES.txt', 'README.md', 'CHANGELOG.md')
    foreach ($framework in $targetFrameworks) {
        $requiredEntries += @("lib/$framework/Lokad.Svg.dll", "lib/$framework/Lokad.Svg.xml")
    }
    foreach ($required in $requiredEntries)
    {
        if ($entries -cnotcontains $required) { throw "Package is missing required entry: $required" }
    }
    if ($entries | Where-Object { $_.StartsWith('runtimes/', [StringComparison]::OrdinalIgnoreCase) }) { throw 'Package must not contain runtime-specific assets.' }
    if ($entries | Where-Object { $_.EndsWith('.svg', [StringComparison]::OrdinalIgnoreCase) }) { throw 'Package must not contain SVG fixtures.' }
    $reader = [IO.StreamReader]::new($archive.GetEntry('README.md').Open())
    try { $readme = $reader.ReadToEnd() }
    finally { $reader.Dispose() }
    $readmeBase = [Uri]::new('https://package.invalid/README.md')
    foreach ($link in [regex]::Matches($readme, '\]\(\s*(?:<(?<target>[^>\r\n]+)>|(?<target>[^\s)]+))')) {
        $target = $link.Groups['target'].Value
        [Uri] $absoluteTarget = $null
        if ([Uri]::TryCreate($target, [UriKind]::Absolute, [ref]$absoluteTarget)) { continue }
        $entryPath = [Uri]::UnescapeDataString([Uri]::new($readmeBase, $target).AbsolutePath.TrimStart('/'))
        if ($entries -cnotcontains $entryPath) { throw "Package README links to missing entry: $target" }
    }
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
    foreach ($framework in $targetFrameworks) {
        if ($symbolEntries -cnotcontains "lib/$framework/Lokad.Svg.pdb") { throw "Symbol package is missing the $framework Lokad.Svg PDB." }
    }
    if ($symbolEntries | Where-Object { $_.StartsWith('runtimes/', [StringComparison]::OrdinalIgnoreCase) }) { throw 'Symbol package must not contain runtime-specific assets.' }
    if ($symbolEntries | Where-Object { $_.EndsWith('.svg', [StringComparison]::OrdinalIgnoreCase) }) { throw 'Symbol package must not contain SVG fixtures.' }
}
finally { $symbolArchive.Dispose() }

New-Item -ItemType Directory -Force -Path $ResultsDirectory | Out-Null
$consumer = Join-Path $ResultsDirectory ('package-consumer-' + [Guid]::NewGuid().ToString('N'))
$template = Join-Path $PSScriptRoot 'Lokad.Svg.PackageSmoke'
New-Item -ItemType Directory -Path $consumer | Out-Null
foreach ($source in Get-ChildItem -LiteralPath $template -File | Where-Object { $_.Extension -in @('.cs', '.csproj') }) {
    Copy-Item -LiteralPath $source.FullName -Destination $consumer
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../tests/Shared/TestMemoryBudget.cs') -Destination (Join-Path $consumer 'TestMemoryBudget.cs')
$consumerAssets = Join-Path $consumer 'assets'
New-Item -ItemType Directory -Path $consumerAssets | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../tests/Research/glyph-fallback/fonts/Primary.ttf') -Destination (Join-Path $consumerAssets 'Primary.ttf')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../tests/Research/glyph-fallback/LICENSE.txt') -Destination (Join-Path $consumerAssets 'LICENSE.txt')
$consumerProject = Join-Path $consumer 'Lokad.Svg.PackageSmoke.csproj'
$packageCache = Join-Path $consumer 'nuget-packages'
$previousPackageCache = $env:NUGET_PACKAGES
$env:NUGET_PACKAGES = $packageCache

try {
    & dotnet restore $consumerProject --source $PackageDirectory --ignore-failed-sources -p:RestoreNoCache=true "-p:LokadSvgPackageVersion=$version"
    if ($LASTEXITCODE -ne 0) { throw 'Could not restore the clean installed-package consumer.' }
    foreach ($framework in $targetFrameworks) {
        & dotnet build $consumerProject -c Release --framework $framework --no-restore "-p:LokadSvgPackageVersion=$version"
        if ($LASTEXITCODE -ne 0) { throw "Could not build the clean $framework installed-package consumer." }
        & dotnet run --project $consumerProject -c Release --framework $framework --no-build "-p:LokadSvgPackageVersion=$version"
        if ($LASTEXITCODE -ne 0) { throw "The clean $framework installed-package consumer failed." }
    }
}
finally { $env:NUGET_PACKAGES = $previousPackageCache }

Write-Output "Package content and consumer scenarios passed. Consumer retained in $consumer."
