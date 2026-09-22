<#
.SYNOPSIS
    Prints the S8 fixture census: document totals, exact/unsupported split,
    distinct-document accounting (variants grouped under their original),
    per-feature-tag coverage and the unsupported reason list.
.DESCRIPTION
    Reads tests/Fixtures/manifest.json from the repository root (resolved by
    walking up from this script) and writes a plain-text report to stdout.
    Exit code is 0. Counts are the admission evidence; percentages are not
    computed because S8 forbids coverage-percentage claims without an agreed
    document-coverage target.
#>
$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
while ($null -ne $root -and -not (Test-Path -LiteralPath (Join-Path $root 'tests/Fixtures/manifest.json'))) {
    $root = Split-Path -Parent $root
}
if ($null -eq $root) { throw 'Repository root not found.' }

$manifest = Get-Content -LiteralPath (Join-Path $root 'tests/Fixtures/manifest.json') -Raw | ConvertFrom-Json
$fixtures = $manifest.fixtures

$exact = @($fixtures | Where-Object { $_.expectedSupport -eq 'exact' })
$unsupported = @($fixtures | Where-Object { $_.expectedSupport -eq 'unsupported' })
$variants = @($fixtures | Where-Object { $null -ne $_.variantOf })
$distinct = $fixtures.Count - $variants.Count

Write-Output "S8 fixture census"
Write-Output ("entries: {0} (exact: {1}, unsupported: {2})" -f $fixtures.Count, $exact.Count, $unsupported.Count)
Write-Output ("distinct documents: {0} (variants: {1})" -f $distinct, $variants.Count)
Write-Output ""
Write-Output "exact entries by feature tag:"
$exact | ForEach-Object { $_.featureTags } | Group-Object | Sort-Object Name | ForEach-Object {
    Write-Output ("  {0}: {1}" -f $_.Name, $_.Count)
}
Write-Output ""
Write-Output "unsupported entries:"
foreach ($entry in $unsupported) {
    Write-Output ("  {0} [{1}]: {2}" -f $entry.id, ($entry.featureTags -join ','), $entry.unsupportedReason)
}