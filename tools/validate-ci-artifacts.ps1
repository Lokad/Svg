param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Windows', 'Linux')]
    [string] $Platform,
    [Parameter(Mandatory = $true)]
    [string] $ChromeDirectory,
    [Parameter(Mandatory = $true)]
    [string] $MemoryDirectory
)

$ErrorActionPreference = 'Stop'

function Fail([string] $Message) { throw "CI artifact validation failed: $Message" }

function Get-Hash([string] $Path)
{
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { Fail "Missing file: $Path" }
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-TextHash([string] $Path)
{
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { Fail "Missing file: $Path" }
    $text = [IO.File]::ReadAllText($Path).Replace("`r`n", "`n").Replace("`r", "`n")
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.UTF8Encoding]::new($false).GetBytes($text))).ToLowerInvariant()
}

function Get-RequiredProperty([object] $Object, [string] $Name, [string] $Context)
{
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property -or $null -eq $property.Value -or ($property.Value -is [string] -and [string]::IsNullOrWhiteSpace($property.Value))) { Fail "$Context is missing $Name." }
    return $property.Value
}

function Assert-Equal([object] $Expected, [object] $Actual, [string] $Context)
{
    if ([string] $Expected -cne [string] $Actual) { Fail "$Context is '$Actual', expected '$Expected'." }
}

function Assert-Hash([object] $Actual, [string] $Expected, [string] $Context)
{
    $value = [string] $Actual
    if ($value -notmatch '^[0-9a-f]{64}$') { Fail "$Context is not a lowercase SHA-256." }
    Assert-Equal $Expected $value $Context
}

function Get-SingleFile([string] $Directory, [string] $Filter, [string] $Context)
{
    if (-not (Test-Path -LiteralPath $Directory -PathType Container)) { Fail "Missing $Context directory: $Directory" }
    $files = @(Get-ChildItem -LiteralPath $Directory -File -Filter $Filter)
    if ($files.Count -ne 1) { Fail "$Context must contain exactly one $Filter file." }
    return $files[0]
}

function Assert-PlatformFingerprint([object] $Fingerprint, [string] $Context)
{
    $operatingSystem = [string](Get-RequiredProperty $Fingerprint 'operatingSystem' $Context)
    if ($Platform -eq 'Windows' -and -not $operatingSystem.StartsWith('Microsoft Windows', [StringComparison]::OrdinalIgnoreCase)) { Fail "$Context is not a Windows fingerprint: $operatingSystem" }
    if ($Platform -eq 'Linux' -and $operatingSystem -notmatch 'Linux') { Fail "$Context is not a Linux fingerprint: $operatingSystem" }
    Assert-Equal 'X64' (Get-RequiredProperty $Fingerprint 'processArchitecture' $Context) "$Context process architecture"
    Assert-Equal ([Runtime.InteropServices.RuntimeInformation]::FrameworkDescription) (Get-RequiredProperty $Fingerprint 'runtimeFramework' $Context) "$Context runtime framework"
}

function Test-ChromeArtifact
{
    $pinPath = Join-Path $PSScriptRoot $(if ($Platform -eq 'Windows') { 'chrome-for-testing.json' } else { 'chrome-for-testing.linux64.json' })
    $pin = Get-Content -LiteralPath $pinPath -Raw | ConvertFrom-Json
    $fingerprintFile = Get-SingleFile $ChromeDirectory 'pinned-chrome-smoke-*.json' 'Chrome smoke artifact'
    $fingerprint = Get-Content -LiteralPath $fingerprintFile.FullName -Raw | ConvertFrom-Json
    Assert-Equal 2 (Get-RequiredProperty $fingerprint 'schemaVersion' 'Chrome fingerprint') 'Chrome fingerprint schema version'
    Assert-Hash (Get-RequiredProperty $fingerprint 'pinSha256' 'Chrome fingerprint') (Get-Hash $pinPath) 'Chrome pin hash'
    foreach ($name in @('version', 'chromiumRevision', 'platform', 'archiveSha256', 'executableSha256', 'captureRecipeRevision'))
    {
        $fingerprintName = if ($name -eq 'version') { 'pinVersion' } else { $name }
        Assert-Equal (Get-RequiredProperty $pin $name 'Chrome pin') (Get-RequiredProperty $fingerprint $fingerprintName 'Chrome fingerprint') "Chrome $name"
    }

    $smokeScript = Join-Path $PSScriptRoot 'test-chrome.ps1'
    $setupScript = Join-Path $PSScriptRoot 'setup-chrome.ps1'
    $captureTool = Join-Path $PSScriptRoot 'Lokad.Svg.Chrome/bin/Release/net10.0/Lokad.Svg.Chrome.dll'
    $comparisonTool = Join-Path $PSScriptRoot 'Lokad.Svg.VisualDiff/bin/Release/net10.0/Lokad.Svg.VisualDiff.dll'
    $fixture = Join-Path $PSScriptRoot '../tests/Fixtures/original/solid-red.svg'
    $reference = Join-Path $PSScriptRoot '../tests/References/original-solid-red.png'
    Assert-Hash (Get-RequiredProperty $fingerprint 'smokeScriptSha256' 'Chrome fingerprint') (Get-TextHash $smokeScript) 'Chrome smoke-script hash'
    Assert-Hash (Get-RequiredProperty $fingerprint 'setupScriptSha256' 'Chrome fingerprint') (Get-TextHash $setupScript) 'Chrome setup-script hash'
    Assert-Hash (Get-RequiredProperty $fingerprint 'captureToolSha256' 'Chrome fingerprint') (Get-Hash $captureTool) 'Chrome capture-tool hash'
    Assert-Hash (Get-RequiredProperty $fingerprint 'comparisonToolSha256' 'Chrome fingerprint') (Get-Hash $comparisonTool) 'Chrome comparison-tool hash'
    Assert-Hash (Get-RequiredProperty $fingerprint 'fixtureSha256' 'Chrome fingerprint') (Get-Hash $fixture) 'Chrome fixture hash'
    $referenceHash = Get-Hash $reference
    Assert-Hash (Get-RequiredProperty $fingerprint 'referenceSha256' 'Chrome fingerprint') $referenceHash 'Chrome reference hash'
    Assert-PlatformFingerprint $fingerprint 'Chrome fingerprint'
    Assert-Equal 3 (Get-RequiredProperty $fingerprint 'freshProcessCaptures' 'Chrome fingerprint') 'Chrome fresh-process capture count'
    Assert-Equal 120000 (Get-RequiredProperty $fingerprint 'captureTimeoutMilliseconds' 'Chrome fingerprint') 'Chrome capture timeout'
    foreach ($name in @('locale', 'timeZone', 'headlessMode', 'gpuPolicy', 'sandbox', 'resourcePolicy', 'colorSpacePolicy', 'fontPolicy', 'background')) { [void](Get-RequiredProperty $fingerprint $name 'Chrome fingerprint') }
    Assert-Equal 'new' $fingerprint.headlessMode 'Chrome headless mode'
    Assert-Equal 'disabled' $fingerprint.gpuPolicy 'Chrome GPU policy'
    Assert-Equal 'enabled' $fingerprint.sandbox 'Chrome sandbox policy'
    Assert-Equal 'transparent' $fingerprint.background 'Chrome background policy'
    Assert-Equal 'data URLs only; http, https, file, ftp, ws and wss are blocked through DevTools Network.setBlockedURLs' $fingerprint.resourcePolicy 'Chrome resource policy'
    Assert-Equal 'browser-default PNG screenshot; no DevTools color-space override' $fingerprint.colorSpacePolicy 'Chrome color-space policy'
    Assert-Equal 'not-exercised: fixed smoke SVG has no text' $fingerprint.fontPolicy 'Chrome font policy'
    $viewport = Get-RequiredProperty $fingerprint 'viewport' 'Chrome fingerprint'
    Assert-Equal 2 (Get-RequiredProperty $viewport 'width' 'Chrome viewport') 'Chrome viewport width'
    Assert-Equal 2 (Get-RequiredProperty $viewport 'height' 'Chrome viewport') 'Chrome viewport height'
    Assert-Equal 1 (Get-RequiredProperty $viewport 'deviceScaleFactor' 'Chrome viewport') 'Chrome viewport scale'

    $candidateNames = @($fingerprint.candidatePngs)
    $candidateHashes = @($fingerprint.candidateSha256)
    if ($candidateNames.Count -ne 3 -or $candidateHashes.Count -ne 3) { Fail 'Chrome fingerprint must list exactly three candidates and hashes.' }
    for ($index = 0; $index -lt 3; $index++)
    {
        $candidateName = [string]$candidateNames[$index]
        if ([IO.Path]::GetFileName($candidateName) -cne $candidateName) { Fail "Chrome candidate name is not a file name: $candidateName" }
        $candidateHash = [string]$candidateHashes[$index]
        Assert-Hash $candidateHash $referenceHash "Chrome candidate $candidateName hash"
        Assert-Equal $candidateHash (Get-Hash (Join-Path $ChromeDirectory $candidateName)) "Chrome candidate $candidateName file hash"
    }
}

function Test-MemoryArtifact
{
    $summaryFile = Get-SingleFile $MemoryDirectory '*-summary.json' 'Memory probe artifact'
    $summary = Get-Content -LiteralPath $summaryFile.FullName -Raw | ConvertFrom-Json
    Assert-Equal 2 (Get-RequiredProperty $summary 'schemaVersion' 'Memory summary') 'Memory summary schema version'
    $runId = [string](Get-RequiredProperty $summary 'runId' 'Memory summary')
    if ($runId -notmatch '^[0-9a-f]{32}$') { Fail 'Memory summary run id is not a lowercase GUID without separators.' }
    $scenarioScript = Join-Path $PSScriptRoot 'test-memory-probe.ps1'
    $probeTool = Join-Path $PSScriptRoot 'Lokad.Svg.MemoryProbe/bin/Release/net10.0/Lokad.Svg.MemoryProbe.dll'
    $rendererAssembly = Join-Path (Split-Path -Parent $probeTool) 'Lokad.Svg.dll'
    $fixture = Join-Path $PSScriptRoot '../tests/Fixtures/original/solid-red.svg'
    Assert-Hash (Get-RequiredProperty $summary 'scenarioScriptSha256' 'Memory summary') (Get-TextHash $scenarioScript) 'Memory scenario-script hash'
    Assert-Hash (Get-RequiredProperty $summary 'probeToolSha256' 'Memory summary') (Get-Hash $probeTool) 'Memory probe-tool hash'
    Assert-Hash (Get-RequiredProperty $summary 'rendererAssemblySha256' 'Memory summary') (Get-Hash $rendererAssembly) 'Memory renderer hash'
    $fixtureHash = Get-Hash $fixture
    Assert-Hash (Get-RequiredProperty $summary 'fixtureSha256' 'Memory summary') $fixtureHash 'Memory fixture hash'
    $runtimeFingerprint = Get-RequiredProperty $summary 'runtimeFingerprint' 'Memory summary'
    Assert-PlatformFingerprint $runtimeFingerprint 'Memory summary'

    $expectedOutcomes = [ordered]@{
        success = 'success'; limit = 'svg-error'; 'xml-workspace' = 'svg-error'
        'xml-reader-text-1' = 'xml-reader-measured'; 'xml-reader-text-2' = 'xml-reader-measured'; 'xml-reader-text-3' = 'xml-reader-measured'
        'xml-attributes' = 'svg-error'; 'xml-reader-1' = 'xml-reader-measured'; 'xml-reader-2' = 'xml-reader-measured'; 'xml-reader-3' = 'xml-reader-measured'
        'xml-reader-attribute-values-1' = 'xml-reader-measured'; 'xml-reader-attribute-values-2' = 'xml-reader-measured'; 'xml-reader-attribute-values-3' = 'xml-reader-measured'
        cancellation = 'cancelled'; 'parser-cancellation' = 'cancelled'
    }
    $reports = @($summary.reports)
    if ($reports.Count -ne $expectedOutcomes.Count) { Fail "Memory summary contains $($reports.Count) reports, expected $($expectedOutcomes.Count)." }
    $reportsByName = @{}
    foreach ($entry in $reports)
    {
        $name = [string](Get-RequiredProperty $entry 'name' 'Memory report entry')
        if (-not $expectedOutcomes.Contains($name) -or $reportsByName.ContainsKey($name)) { Fail "Memory summary has an unexpected or duplicate report: $name" }
        $report = Get-RequiredProperty $entry 'report' "Memory report $name"
        $reportsByName[$name] = $report
        Assert-Equal $expectedOutcomes[$name] (Get-RequiredProperty $report 'outcome' "Memory report $name") "Memory report $name outcome"
        Assert-Equal $runtimeFingerprint.operatingSystem (Get-RequiredProperty $report 'operatingSystem' "Memory report $name") "Memory report $name operating system"
        Assert-Equal $runtimeFingerprint.processArchitecture (Get-RequiredProperty $report 'processArchitecture' "Memory report $name") "Memory report $name process architecture"
        Assert-Equal $runtimeFingerprint.runtimeFramework (Get-RequiredProperty $report 'runtimeFramework' "Memory report $name") "Memory report $name runtime framework"
        if ([int64](Get-RequiredProperty $report 'inputBytes' "Memory report $name") -le 0) { Fail "Memory report $name has no input bytes." }
        $inputHash = [string](Get-RequiredProperty $report 'inputSha256' "Memory report $name")
        if ($inputHash -notmatch '^[0-9a-f]{64}$') { Fail "Memory report $name input hash is not a lowercase SHA-256." }
        foreach ($suffix in @('stdout.txt', 'stderr.txt')) { if (-not (Test-Path -LiteralPath (Join-Path $MemoryDirectory "$runId-$name.$suffix") -PathType Leaf)) { Fail "Memory report $name did not retain $suffix." } }
    }
    foreach ($name in $expectedOutcomes.Keys) { if (-not $reportsByName.ContainsKey($name)) { Fail "Memory summary is missing report $name." } }
    foreach ($name in @('limit', 'xml-workspace', 'xml-attributes')) { Assert-Equal 'LimitExceeded' (Get-RequiredProperty $reportsByName[$name] 'svgErrorCode' "Memory report $name") "Memory report $name error code" }
    foreach ($name in @($expectedOutcomes.Keys | Where-Object { $_ -ne 'success' })) { if (Test-Path -LiteralPath (Join-Path $MemoryDirectory "$runId-$name.png")) { Fail "Memory report $name unexpectedly wrote a PNG." } }

    $inputFiles = @{
        success = $fixture; limit = $fixture; 'xml-workspace' = (Join-Path $MemoryDirectory "$runId-xml-workspace.svg")
        'xml-reader-text-1' = (Join-Path $MemoryDirectory "$runId-xml-workspace.svg"); 'xml-reader-text-2' = (Join-Path $MemoryDirectory "$runId-xml-workspace.svg"); 'xml-reader-text-3' = (Join-Path $MemoryDirectory "$runId-xml-workspace.svg")
        'xml-attributes' = (Join-Path $MemoryDirectory "$runId-xml-attributes.svg"); 'xml-reader-1' = (Join-Path $MemoryDirectory "$runId-xml-attributes.svg"); 'xml-reader-2' = (Join-Path $MemoryDirectory "$runId-xml-attributes.svg"); 'xml-reader-3' = (Join-Path $MemoryDirectory "$runId-xml-attributes.svg")
        'xml-reader-attribute-values-1' = (Join-Path $MemoryDirectory "$runId-xml-reader-attribute-values.svg"); 'xml-reader-attribute-values-2' = (Join-Path $MemoryDirectory "$runId-xml-reader-attribute-values.svg"); 'xml-reader-attribute-values-3' = (Join-Path $MemoryDirectory "$runId-xml-reader-attribute-values.svg")
        cancellation = (Join-Path $MemoryDirectory "$runId-cancellation.svg"); 'parser-cancellation' = (Join-Path $MemoryDirectory "$runId-parser-cancellation.svg")
    }
    foreach ($name in $expectedOutcomes.Keys)
    {
        $inputPath = $inputFiles[$name]
        Assert-Equal ([IO.FileInfo]::new($inputPath).Length) (Get-RequiredProperty $reportsByName[$name] 'inputBytes' "Memory report $name") "Memory report $name input byte count"
        Assert-Hash (Get-RequiredProperty $reportsByName[$name] 'inputSha256' "Memory report $name") (Get-Hash $inputPath) "Memory report $name input hash"
    }
    $successOutput = Join-Path $MemoryDirectory "$runId-success.png"
    Assert-Equal ([IO.FileInfo]::new($successOutput).Length) (Get-RequiredProperty $reportsByName['success'] 'pngBytes' 'Memory success report') 'Memory success PNG byte count'
}

Test-ChromeArtifact
Test-MemoryArtifact
Write-Output "Validated $Platform CI artifact contents. This checks artifact consistency only; retain the GitHub run URL, commit and successful conclusion separately before treating it as remote evidence."
