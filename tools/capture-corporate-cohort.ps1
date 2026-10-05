param(
    [Parameter(Mandatory = $true)] [string] $OutputDirectory,
    [Parameter(Mandatory = $true)] [string] $CacheDirectory,
    [string[]] $CaseId = @()
)
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
$manifest = Get-Content -LiteralPath (Join-Path $repository 'tests/References/inventory/corporate-cohort-v1.json') -Raw | ConvertFrom-Json
$pin = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'chrome-for-testing.json') -Raw | ConvertFrom-Json
foreach ($property in $manifest.oraclePin.PSObject.Properties) {
    if ($pin.($property.Name) -cne $property.Value) { throw "Chrome pin differs from the frozen cohort: $($property.Name)" }
}
$variants = @($manifest.documents | ForEach-Object { $_.variants })
if ($CaseId.Count -gt 0) {
    foreach ($id in $CaseId) {
        if (@($variants | Where-Object id -EQ $id).Count -ne 1) { throw "Unknown frozen variant: $id" }
    }
    $variants = @($variants | Where-Object { $_.id -in $CaseId })
}
$output = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $output | Out-Null
$results = @()
foreach ($variant in $variants) {
    $inputFile = Join-Path $repository $variant.oracleInput
    $inputHash = (Get-FileHash -LiteralPath $inputFile -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($inputHash -ne $variant.oracleInputSha256) { throw "Frozen oracle input changed: $($variant.id)" }
    $captures = @()
    $failure = $null
    try {
        foreach ($repeat in 1..3) {
            $png = Join-Path $output ($variant.id + '-r' + $repeat + '.png')
            & (Join-Path $PSScriptRoot 'capture-chrome.ps1') -InputPath $inputFile -OutputPath $png -Width $variant.width -Height $variant.height -Background $variant.background -CacheDirectory $CacheDirectory
            $launch = (Get-Content -LiteralPath ($png + '.stderr.txt') -Raw | ConvertFrom-Json).chromeLaunch
            if ($launch.startupHealth -ne 'healthy' -or -not $launch.profileCleaned -or $launch.stderrTruncated) { throw 'Capture diagnostics do not establish healthy startup, cleanup and complete stderr.' }
            $captures += [ordered]@{
                pngSha256 = (Get-FileHash -LiteralPath $png -Algorithm SHA256).Hash.ToLowerInvariant()
                startupHealth = $launch.startupHealth
                profileCleaned = $launch.profileCleaned
                stderrTruncated = $launch.stderrTruncated
            }
        }
        if (@($captures.pngSha256 | Select-Object -Unique).Count -ne 1) { throw 'Fresh-process PNGs are not byte-identical.' }
    }
    catch { $failure = $_.Exception.Message }
    $results += [ordered]@{
        id = $variant.id
        oracleInputSha256 = $inputHash
        width = $variant.width
        height = $variant.height
        background = $variant.background
        captures = $captures
        failure = $failure
    }
    # Retain completed cases and failure diagnostics if a later capture fails.
    [ordered]@{ cohortId = $manifest.cohortId; oraclePin = $manifest.oraclePin; results = $results } | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $output 'captures.json') -Encoding utf8
    Write-Output ($variant.id + ': ' + $(if ($null -eq $failure) { '3 stable captures' } else { 'capture failed' }))
}
if (@($results | Where-Object { $null -ne $_.failure }).Count -gt 0) { throw 'One or more frozen variants failed capture; see captures.json and adjacent stderr reports.' }
