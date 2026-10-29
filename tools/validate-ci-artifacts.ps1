param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Windows', 'Linux')]
    [string] $Platform,
    [Parameter(Mandatory = $true)]
    [string] $ChromeDirectory,
    [Parameter(Mandatory = $true)]
    [string] $MemoryDirectory,
    [switch] $RequireCurrentHost,
    [ValidateSet('net11.0', 'net10.0')]
    [string] $TargetFramework = 'net11.0'
)

$ErrorActionPreference = 'Stop'

function Fail([string] $Message) { throw "CI artifact validation failed: $Message" }

function Get-Hash([string] $Path)
{
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { Fail "Missing file: $Path" }
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
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

function Assert-PlatformOsArchitecture([object] $Fingerprint, [string] $Context)
{
    $operatingSystem = [string](Get-RequiredProperty $Fingerprint 'operatingSystem' $Context)
    # Linux descriptions can be distribution names without the word Linux.
    # The Chrome pin establishes the declared platform; OS descriptions must agree across reports.
    $windowsDescription = $operatingSystem -match '\bWindows\b'
    if (($Platform -eq 'Windows') -ne $windowsDescription) { Fail "$Context operating system does not match the declared $Platform platform." }
    if ($RequireCurrentHost) {
        $expectedPlatform = [Runtime.InteropServices.OSPlatform]::Create($Platform.ToUpperInvariant())
        if (-not [Runtime.InteropServices.RuntimeInformation]::IsOSPlatform($expectedPlatform)) { Fail "Validator worker does not run $Platform." }
        Assert-Equal ([Runtime.InteropServices.RuntimeInformation]::OSDescription) $operatingSystem "$Context operating system"
    }
    Assert-Equal 'X64' (Get-RequiredProperty $Fingerprint 'processArchitecture' $Context) "$Context process architecture"
}

function Assert-HostRuntime([object] $Framework, [string] $Context)
{
    if ([string]$Framework -cnotmatch '^\.NET \d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$') { Fail "$Context is not a .NET runtime framework." }
    if ($RequireCurrentHost) { Assert-Equal ([Runtime.InteropServices.RuntimeInformation]::FrameworkDescription) $Framework $Context }
}

function Test-ChromeArtifact
{
    $pinPath = Join-Path $PSScriptRoot $(if ($Platform -eq 'Windows') { 'chrome-for-testing.json' } else { 'chrome-for-testing.linux64.json' })
    $pin = Get-Content -LiteralPath $pinPath -Raw | ConvertFrom-Json
    $fingerprintFile = Get-SingleFile $ChromeDirectory 'pinned-chrome-smoke-*.json' 'Chrome smoke artifact'
    $fingerprint = Get-Content -LiteralPath $fingerprintFile.FullName -Raw | ConvertFrom-Json
    Assert-Equal 4 (Get-RequiredProperty $fingerprint 'schemaVersion' 'Chrome fingerprint') 'Chrome fingerprint schema version'
    Assert-Hash (Get-RequiredProperty $fingerprint 'pinSha256' 'Chrome fingerprint') (Get-Hash $pinPath) 'Chrome pin hash'
    foreach ($name in @('version', 'chromiumRevision', 'platform', 'archiveSha256', 'executableSha256', 'captureRecipeRevision'))
    {
        $fingerprintName = if ($name -eq 'version') { 'pinVersion' } else { $name }
        Assert-Equal (Get-RequiredProperty $pin $name 'Chrome pin') (Get-RequiredProperty $fingerprint $fingerprintName 'Chrome fingerprint') "Chrome $name"
    }

    $comparisonTool = Join-Path $PSScriptRoot "Lokad.Svg.VisualDiff/bin/Release/$TargetFramework/Lokad.Svg.VisualDiff.dll"
    $fixture = Join-Path $PSScriptRoot '../tests/Fixtures/original/solid-red.svg'
    $reference = Join-Path $PSScriptRoot '../tests/References/original-solid-red.png'
    Assert-Hash (Get-RequiredProperty $fingerprint 'fixtureSha256' 'Chrome fingerprint') (Get-Hash $fixture) 'Chrome fixture hash'
    $referenceHash = Get-Hash $reference
    Assert-Hash (Get-RequiredProperty $fingerprint 'referenceSha256' 'Chrome fingerprint') $referenceHash 'Chrome reference hash'
    Assert-PlatformOsArchitecture $fingerprint 'Chrome fingerprint'
    Assert-HostRuntime (Get-RequiredProperty $fingerprint 'hostRuntimeFramework' 'Chrome fingerprint') 'Chrome host runtime framework'
    $chromeRuntimes = @(Get-RequiredProperty $fingerprint 'dotnetRuntimes' 'Chrome fingerprint')
    if ($chromeRuntimes.Count -eq 0) { Fail 'Chrome fingerprint lists no .NET runtimes.' }
    foreach ($runtimeVersion in $chromeRuntimes) { if ([string]$runtimeVersion -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') { Fail "Chrome fingerprint lists a malformed runtime version: $runtimeVersion" } }
    $captureToolFramework = [string](Get-RequiredProperty $fingerprint 'captureToolRuntimeFramework' 'Chrome fingerprint')
    $captureToolVersion = $captureToolFramework -replace '^\.NET\s+', ''
    if ($chromeRuntimes -cnotcontains $captureToolVersion) { Fail "Chrome capture-tool runtime $captureToolFramework is not among the recorded dotnet runtimes." }
    Assert-Equal 3 (Get-RequiredProperty $fingerprint 'freshProcessCaptures' 'Chrome fingerprint') 'Chrome fresh-process capture count'
    Assert-Equal 120000 (Get-RequiredProperty $fingerprint 'captureTimeoutMilliseconds' 'Chrome fingerprint') 'Chrome capture timeout'
    # CultureInfo.InvariantCulture.Name is empty; it is a valid recorded locale.
    $locale = $fingerprint.PSObject.Properties['locale']
    if ($null -eq $locale -or $null -eq $locale.Value) { Fail 'Chrome fingerprint is missing locale.' }
    foreach ($name in @('timeZone', 'headlessMode', 'gpuPolicy', 'sandbox', 'resourcePolicy', 'colorSpacePolicy', 'fontPolicy', 'background')) { [void](Get-RequiredProperty $fingerprint $name 'Chrome fingerprint') }
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
    $stableHash = $null
    $diagnostics = @(Get-RequiredProperty $fingerprint 'captureDiagnostics' 'Chrome fingerprint')
    if ($diagnostics.Count -ne 3) { Fail 'Chrome fingerprint must list exactly three capture diagnostics.' }
    for ($index = 0; $index -lt 3; $index++)
    {
        $candidateName = [string]$candidateNames[$index]
        if ([IO.Path]::GetFileName($candidateName) -cne $candidateName) { Fail "Chrome candidate name is not a file name: $candidateName" }
        $candidatePath = Join-Path $ChromeDirectory $candidateName
        $candidateHash = [string]$candidateHashes[$index]
        Assert-Hash $candidateHash (Get-Hash $candidatePath) "Chrome candidate $candidateName file hash"
        if ($null -eq $stableHash) { $stableHash = $candidateHash }
        elseif ($candidateHash -cne $stableHash) { Fail "Chrome candidate $candidateName is not byte-identical to the first fresh-process capture." }
        & dotnet $comparisonTool $reference $candidatePath | Out-Null
        if ($LASTEXITCODE -ne 0) { Fail "Chrome candidate $candidateName differs from its reference pixels." }
        $diagnostic = $diagnostics[$index]
        $stderrName = [string](Get-RequiredProperty $diagnostic 'stderrFile' 'Chrome diagnostic')
        if ($stderrName -cne ($candidateName + '.stderr.txt')) { Fail 'Chrome diagnostic filename does not match its candidate.' }
        $stderrPath = Join-Path $ChromeDirectory $stderrName
        if ((Get-Item -LiteralPath $stderrPath).Length -gt 262144) { Fail 'Chrome stderr exceeds the retained diagnostic bound.' }
        $launch = (Get-Content -LiteralPath $stderrPath -Raw | ConvertFrom-Json).chromeLaunch
        Assert-Equal 1 (Get-RequiredProperty $launch 'schemaVersion' 'Chrome launch') 'Chrome launch schema'
        Assert-Equal 'healthy' (Get-RequiredProperty $launch 'startupHealth' 'Chrome launch') 'Chrome startup health'
        Assert-Equal 0 (Get-RequiredProperty $launch 'sandboxAccessFailures' 'Chrome launch') 'Chrome sandbox access failures'
        Assert-Equal True (Get-RequiredProperty $launch 'profileCleaned' 'Chrome launch') 'Chrome profile cleanup'
        Assert-Equal ([IO.Path]::GetFileName($pin.executableRelativePath)) (Get-RequiredProperty $launch 'executableName' 'Chrome launch') 'Chrome executable identity'
        $crashes = [long](Get-RequiredProperty $launch 'networkServiceCrashes' 'Chrome launch')
        if ($crashes -lt 0 -or $crashes -ge 2) { Fail 'Chrome network service repeatedly crashed.' }
        $elapsed = [long](Get-RequiredProperty $launch 'elapsedMilliseconds' 'Chrome launch')
        $ready = [long](Get-RequiredProperty $launch 'debuggerReadyMilliseconds' 'Chrome launch')
        $captureElapsed = [long](Get-RequiredProperty $diagnostic 'captureElapsedMilliseconds' 'Chrome diagnostic')
        if ($ready -lt 0 -or $elapsed -lt $ready -or $captureElapsed -lt $elapsed -or $captureElapsed -gt $fingerprint.captureTimeoutMilliseconds) { Fail 'Chrome diagnostic timings are inconsistent.' }
        if ($null -eq $launch.PSObject.Properties['stderr'] -or $null -eq $launch.PSObject.Properties['stderrTruncated']) { Fail 'Chrome launch is missing bounded stderr.' }
        if ($launch.stderr -match 'Sandbox cannot access executable' -or [regex]::Matches($launch.stderr, 'Network service crashed or was terminated').Count -ge 2) { Fail 'Chrome stderr contradicts healthy startup.' }
    }
    return $fingerprint
}

function Test-MemoryArtifact([object] $ChromeFingerprint)
{
    $summaryFile = Get-SingleFile $MemoryDirectory '*-summary.json' 'Memory probe artifact'
    $summary = Get-Content -LiteralPath $summaryFile.FullName -Raw | ConvertFrom-Json
    Assert-Equal 3 (Get-RequiredProperty $summary 'schemaVersion' 'Memory summary') 'Memory summary schema version'
    $runId = [string](Get-RequiredProperty $summary 'runId' 'Memory summary')
    if ($runId -notmatch '^[0-9a-f]{32}$') { Fail 'Memory summary run id is not a lowercase GUID without separators.' }
    $fixture = Join-Path $PSScriptRoot '../tests/Fixtures/original/solid-red.svg'
    $fixtureHash = Get-Hash $fixture
    Assert-Hash (Get-RequiredProperty $summary 'fixtureSha256' 'Memory summary') $fixtureHash 'Memory fixture hash'
    $runtimeFingerprint = Get-RequiredProperty $summary 'runtimeFingerprint' 'Memory summary'
    Assert-PlatformOsArchitecture $runtimeFingerprint 'Memory summary'
    Assert-Equal $ChromeFingerprint.operatingSystem $runtimeFingerprint.operatingSystem 'Memory summary operating system'
    $hostFramework = Get-RequiredProperty $summary 'hostRuntimeFramework' 'Memory summary'
    Assert-HostRuntime $hostFramework 'Memory host runtime framework'
    Assert-Equal $ChromeFingerprint.hostRuntimeFramework $hostFramework 'Memory host runtime framework'
    $dotnetRuntimes = @(Get-RequiredProperty $summary 'dotnetRuntimes' 'Memory summary')
    if ($dotnetRuntimes.Count -eq 0) { Fail 'Memory summary lists no .NET runtimes.' }
    foreach ($runtimeVersion in $dotnetRuntimes) { if ([string]$runtimeVersion -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') { Fail "Memory summary lists a malformed runtime version: $runtimeVersion" } }
    $childFramework = [string](Get-RequiredProperty $runtimeFingerprint 'runtimeFramework' 'Memory summary')
    $childVersion = $childFramework -replace '^\.NET\s+', ''
    if ($dotnetRuntimes -cnotcontains $childVersion) { Fail "Memory child runtime $childFramework is not among the recorded dotnet runtimes." }

    $expectedOutcomes = [ordered]@{
        success = 'success'; limit = 'svg-error'; image = 'success'; 'image-denied' = 'svg-error'; pattern = 'success'; 'pattern-memory-denied' = 'svg-error'
        'xml-workspace' = 'svg-error'
        'xml-reader-text-1' = 'xml-reader-measured'; 'xml-reader-text-2' = 'xml-reader-measured'; 'xml-reader-text-3' = 'xml-reader-measured'
        'xml-attributes' = 'svg-error'; 'xml-reader-1' = 'xml-reader-measured'; 'xml-reader-2' = 'xml-reader-measured'; 'xml-reader-3' = 'xml-reader-measured'
        'xml-reader-attribute-values-1' = 'xml-reader-measured'; 'xml-reader-attribute-values-2' = 'xml-reader-measured'; 'xml-reader-attribute-values-3' = 'xml-reader-measured'
        cancellation = 'cancelled'; 'parser-cancellation' = 'cancelled'; 'background-cancellation' = 'cancelled'
        'font-discovery-deep-groups' = 'success'; 'font-discovery-deep-groups-denied' = 'svg-error'
        'font-discovery-empty-texts' = 'success'; 'font-discovery-distinct-requests' = 'svg-error'; 'font-discovery-cancelled' = 'cancelled'
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
    foreach ($name in @('limit', 'image-denied', 'pattern-memory-denied', 'xml-workspace', 'xml-attributes', 'font-discovery-deep-groups-denied')) { Assert-Equal 'LimitExceeded' (Get-RequiredProperty $reportsByName[$name] 'svgErrorCode' "Memory report $name") "Memory report $name error code" }
    Assert-Equal 'UnsupportedFeature' (Get-RequiredProperty $reportsByName['font-discovery-distinct-requests'] 'svgErrorCode' "Memory report font-discovery-distinct-requests") "Memory report font-discovery-distinct-requests error code"
    foreach ($name in @('font-discovery-deep-groups', 'font-discovery-deep-groups-denied', 'font-discovery-empty-texts', 'font-discovery-distinct-requests', 'font-discovery-cancelled')) { Assert-Equal 0 ([int64](Get-RequiredProperty $reportsByName[$name] 'discoveryCurrentBytes' "Memory report $name")) "Memory report $name retained ledger bytes" }
    foreach ($name in @('font-discovery-deep-groups-denied', 'font-discovery-cancelled')) { if ($reportsByName[$name].PSObject.Properties.Name -contains 'discoveryRequestCount') { Fail "Memory report $name unexpectedly discovered requests." } }

    foreach ($name in @($expectedOutcomes.Keys | Where-Object { $_ -notin @('success', 'image', 'pattern') })) { if (Test-Path -LiteralPath (Join-Path $MemoryDirectory "$runId-$name.png")) { Fail "Memory report $name unexpectedly wrote a PNG." } }

    $inputFiles = @{
        success = $fixture; limit = $fixture; image = (Join-Path $MemoryDirectory "$runId-image.svg"); 'image-denied' = (Join-Path $MemoryDirectory "$runId-image.svg"); pattern = (Join-Path $MemoryDirectory "$runId-pattern.svg"); 'pattern-memory-denied' = (Join-Path $MemoryDirectory "$runId-pattern.svg"); 'xml-workspace' = (Join-Path $MemoryDirectory "$runId-xml-workspace.svg")
        'xml-reader-text-1' = (Join-Path $MemoryDirectory "$runId-xml-workspace.svg"); 'xml-reader-text-2' = (Join-Path $MemoryDirectory "$runId-xml-workspace.svg"); 'xml-reader-text-3' = (Join-Path $MemoryDirectory "$runId-xml-workspace.svg")
        'xml-attributes' = (Join-Path $MemoryDirectory "$runId-xml-attributes.svg"); 'xml-reader-1' = (Join-Path $MemoryDirectory "$runId-xml-attributes.svg"); 'xml-reader-2' = (Join-Path $MemoryDirectory "$runId-xml-attributes.svg"); 'xml-reader-3' = (Join-Path $MemoryDirectory "$runId-xml-attributes.svg")
        'xml-reader-attribute-values-1' = (Join-Path $MemoryDirectory "$runId-xml-reader-attribute-values.svg"); 'xml-reader-attribute-values-2' = (Join-Path $MemoryDirectory "$runId-xml-reader-attribute-values.svg"); 'xml-reader-attribute-values-3' = (Join-Path $MemoryDirectory "$runId-xml-reader-attribute-values.svg")
        cancellation = (Join-Path $MemoryDirectory "$runId-cancellation.svg"); 'parser-cancellation' = (Join-Path $MemoryDirectory "$runId-parser-cancellation.svg"); 'background-cancellation' = (Join-Path $MemoryDirectory "$runId-background-cancellation.svg")
        'font-discovery-deep-groups' = (Join-Path $MemoryDirectory "$runId-font-discovery-deep-groups.svg"); 'font-discovery-deep-groups-denied' = (Join-Path $MemoryDirectory "$runId-font-discovery-deep-groups.svg")
        'font-discovery-empty-texts' = (Join-Path $MemoryDirectory "$runId-font-discovery-empty-texts.svg"); 'font-discovery-distinct-requests' = (Join-Path $MemoryDirectory "$runId-font-discovery-distinct-requests.svg"); 'font-discovery-cancelled' = (Join-Path $MemoryDirectory "$runId-font-discovery-cancelled.svg")
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

$chromeFingerprint = Test-ChromeArtifact
Test-MemoryArtifact $chromeFingerprint
Write-Output "Validated $Platform CI artifact contents. This checks artifact consistency only; retain the GitHub run URL, commit and successful conclusion separately before treating it as remote evidence."
