param(
    [Parameter(Mandatory = $true)]
    [string] $ResultsDirectory,
    [int] $TimeoutMilliseconds = 120000,
    [int] $XmlReaderProbeRuns = 3,
    [ValidateSet('net11.0', 'net10.0')]
    [string] $TargetFramework = 'net11.0'
)

$ErrorActionPreference = 'Stop'

if ($TimeoutMilliseconds -le 0) { throw 'TimeoutMilliseconds must be positive.' }
if ($XmlReaderProbeRuns -le 0) { throw 'XmlReaderProbeRuns must be positive.' }

$probeTool = Join-Path $PSScriptRoot "Lokad.Svg.MemoryProbe/bin/Release/$TargetFramework/Lokad.Svg.MemoryProbe.dll"
$rendererAssembly = Join-Path (Split-Path -Parent $probeTool) 'Lokad.Svg.dll'
$fixture = Join-Path $PSScriptRoot '../tests/Fixtures/original/solid-red.svg'
if (-not (Test-Path -LiteralPath $probeTool)) { throw "Memory probe is not built: $probeTool" }
if (-not (Test-Path -LiteralPath $rendererAssembly)) { throw "Memory probe renderer is not built: $rendererAssembly" }
if (-not (Test-Path -LiteralPath $fixture)) { throw "Probe fixture is missing: $fixture" }

New-Item -ItemType Directory -Force -Path $ResultsDirectory | Out-Null
$runId = [Guid]::NewGuid().ToString('N')
$reports = [System.Collections.Generic.List[object]]::new()

function Invoke-Probe {
    param(
        [Parameter(Mandatory = $true)] [string] $Name,
        [Parameter(Mandatory = $true)] [string] $InputPath,
        [Parameter(Mandatory = $true)] [string] $Output,
        [Parameter(Mandatory = $true)] [int] $ExpectedExitCode,
        [string[]] $ExtraArguments = @()
    )

    $expectedInputBytes = [IO.FileInfo]::new($InputPath).Length
    $expectedInputSha256 = (Get-FileHash -LiteralPath $InputPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $start = [System.Diagnostics.ProcessStartInfo]::new('dotnet')
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.ArgumentList.Add($probeTool)
    $start.ArgumentList.Add($InputPath)
    $start.ArgumentList.Add($Output)
    foreach ($argument in $ExtraArguments) { $start.ArgumentList.Add($argument) }

    $process = [System.Diagnostics.Process]::Start($start)
    if ($null -eq $process) { throw "Could not start the $Name probe." }
    try {
        $standardOutputTask = $process.StandardOutput.ReadToEndAsync()
        $standardErrorTask = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($TimeoutMilliseconds))
        {
            $process.Kill($true)
            throw "The $Name probe exceeded $TimeoutMilliseconds ms and was terminated."
        }
        $standardOutput = $standardOutputTask.GetAwaiter().GetResult()
        $standardError = $standardErrorTask.GetAwaiter().GetResult()
        [IO.File]::WriteAllText((Join-Path $ResultsDirectory "$runId-$Name.stdout.txt"), $standardOutput)
        [IO.File]::WriteAllText((Join-Path $ResultsDirectory "$runId-$Name.stderr.txt"), $standardError)
        if ($process.ExitCode -ne $ExpectedExitCode)
        {
            throw "The $Name probe exited $($process.ExitCode), expected $ExpectedExitCode. See $ResultsDirectory for its output."
        }
        $values = @{}
        foreach ($line in $standardOutput -split "`r?`n")
        {
            $separator = $line.IndexOf('=')
            if ($separator -gt 0) { $values[$line.Substring(0, $separator)] = $line.Substring($separator + 1) }
        }
        foreach ($requiredField in @('outcome', 'elapsedMilliseconds', 'peakWorkingSetBytes', 'inputBytes', 'inputSha256', 'operatingSystem', 'processArchitecture', 'runtimeFramework'))
        {
            if (-not $values.ContainsKey($requiredField) -or [string]::IsNullOrWhiteSpace($values[$requiredField]))
            {
                throw "The $Name probe did not emit a complete report. See $ResultsDirectory for its output."
            }
        }
        if ($values['inputBytes'] -ne $expectedInputBytes.ToString() -or $values['inputSha256'] -cne $expectedInputSha256)
        {
            throw "The $Name probe did not measure the expected input bytes. See $ResultsDirectory for its output."
        }
        $reports.Add([PSCustomObject]@{
            name = $Name
            report = [PSCustomObject]$values
        })
        return $values
    }
    finally { $process.Dispose() }
}

function Assert-XmlReaderMeasurements {
    param(
        [Parameter(Mandatory = $true)] [string] $Name,
        [Parameter(Mandatory = $true)] [string] $InputPath,
        [Parameter(Mandatory = $true)] [long] $MinimumTouchedCharacters,
        [long] $MinimumTextNodes = 0
    )

    for ($attempt = 1; $attempt -le $XmlReaderProbeRuns; $attempt++)
    {
        $output = Join-Path $ResultsDirectory "$runId-$Name-$attempt.png"
        $measurement = Invoke-Probe -Name "$Name-$attempt" -InputPath $InputPath -Output $output -ExpectedExitCode 0 -ExtraArguments @('--mode', 'xml-reader')
        if ($measurement['outcome'] -ne 'xml-reader-measured' -or -not $measurement.ContainsKey('xmlReaderManagedAllocatedBytes') -or -not $measurement.ContainsKey('xmlReaderWorkspaceBytes') -or -not $measurement.ContainsKey('xmlReaderTouchedCharacters') -or -not $measurement.ContainsKey('xmlReaderTextNodeCount') -or -not $measurement.ContainsKey('xmlReaderSetupManagedAllocatedBytes') -or -not $measurement.ContainsKey('xmlReaderTraversalManagedAllocatedBytes') -or -not $measurement.ContainsKey('xmlReaderAttributeAccessManagedAllocatedBytes') -or (Test-Path -LiteralPath $output))
        {
            throw "The $Name XML reader measurement did not emit a complete no-output report."
        }
        if ([long]$measurement['xmlReaderManagedAllocatedBytes'] -le 0 -or [long]$measurement['xmlReaderTouchedCharacters'] -lt $MinimumTouchedCharacters -or [long]$measurement['xmlReaderTextNodeCount'] -lt $MinimumTextNodes -or [long]$measurement['xmlReaderManagedAllocatedBytes'] -gt [long]$measurement['xmlReaderWorkspaceBytes'] -or [long]$measurement['xmlReaderSetupManagedAllocatedBytes'] -lt 0 -or [long]$measurement['xmlReaderTraversalManagedAllocatedBytes'] -lt 0 -or [long]$measurement['xmlReaderAttributeAccessManagedAllocatedBytes'] -lt 0 -or [long]$measurement['xmlReaderSetupManagedAllocatedBytes'] -gt [long]$measurement['xmlReaderManagedAllocatedBytes'] -or [long]$measurement['xmlReaderTraversalManagedAllocatedBytes'] -gt [long]$measurement['xmlReaderManagedAllocatedBytes'] -or [long]$measurement['xmlReaderAttributeAccessManagedAllocatedBytes'] -gt [long]$measurement['xmlReaderTraversalManagedAllocatedBytes'])
        {
            throw "The $Name XML reader measurement exceeds its renderer workspace allowance."
        }
    }
}

$successOutput = Join-Path $ResultsDirectory "$runId-success.png"
$success = Invoke-Probe -Name 'success' -InputPath $fixture -Output $successOutput -ExpectedExitCode 0 -ExtraArguments @('--maximum-accounted-bytes', '1048576')
if ($success['outcome'] -ne 'success' -or -not (Test-Path -LiteralPath $successOutput)) { throw 'The successful probe did not produce its expected PNG.' }
if (-not $success.ContainsKey('conversionManagedAllocatedBytes') -or [long]$success['conversionManagedAllocatedBytes'] -le 0) { throw 'The successful probe did not report managed conversion allocation.' }

$limitOutput = Join-Path $ResultsDirectory "$runId-limit.png"
$limit = Invoke-Probe -Name 'limit' -InputPath $fixture -Output $limitOutput -ExpectedExitCode 1 -ExtraArguments @('--maximum-accounted-bytes', '1')
if ($limit['outcome'] -ne 'svg-error' -or $limit['svgErrorCode'] -ne 'LimitExceeded' -or (Test-Path -LiteralPath $limitOutput)) { throw 'The limited probe did not reject before output.' }
if (-not $limit.ContainsKey('conversionManagedAllocatedBytes') -or [long]$limit['conversionManagedAllocatedBytes'] -le 0) { throw 'The limited probe did not report managed conversion allocation.' }

$imageInput = Join-Path $ResultsDirectory "$runId-image.svg"
$imagePixel = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg=="
[IO.File]::WriteAllText($imageInput, '<svg xmlns="http://www.w3.org/2000/svg" width="8" height="8"><image href="data:image/png;base64,' + $imagePixel + '" x="0" y="0" width="8" height="8"/></svg>', [Text.UTF8Encoding]::new($false))
$imageBytes = [IO.FileInfo]::new($imageInput).Length
$imageOutput = Join-Path $ResultsDirectory "$runId-image.png"
$image = Invoke-Probe -Name 'image' -InputPath $imageInput -Output $imageOutput -ExpectedExitCode 0
if ($image['outcome'] -ne 'success' -or $image['inputBytes'] -ne $imageBytes.ToString() -or -not (Test-Path -LiteralPath $imageOutput)) { throw 'The embedded-image probe did not convert its solid PNG data URI.' }
if (-not $image.ContainsKey('conversionManagedAllocatedBytes') -or [long]$image['conversionManagedAllocatedBytes'] -le 0) { throw 'The embedded-image probe did not report managed conversion allocation.' }

$imageDeniedOutput = Join-Path $ResultsDirectory "$runId-image-denied.png"
$imageDenied = Invoke-Probe -Name 'image-denied' -InputPath $imageInput -Output $imageDeniedOutput -ExpectedExitCode 1 -ExtraArguments @('--maximum-accounted-bytes', '131122')
if ($imageDenied['outcome'] -ne 'svg-error' -or $imageDenied['svgErrorCode'] -ne 'LimitExceeded' -or $imageDenied['inputBytes'] -ne $imageBytes.ToString() -or (Test-Path -LiteralPath $imageDeniedOutput)) { throw 'The storage-denied embedded-image probe did not reject before output.' }

$patternInput = Join-Path $ResultsDirectory "$runId-pattern.svg"
[IO.File]::WriteAllText($patternInput, '<svg xmlns="http://www.w3.org/2000/svg" width="64" height="64"><defs><pattern id="p" width="256" height="256" patternUnits="userSpaceOnUse"><rect width="256" height="256" fill="red"/></pattern></defs><rect x="0" y="0" width="64" height="64" fill="url(#p)"/></svg>', [Text.UTF8Encoding]::new($false))
$patternBytes = [IO.FileInfo]::new($patternInput).Length
$patternOutput = Join-Path $ResultsDirectory "$runId-pattern.png"
$pattern = Invoke-Probe -Name 'pattern' -InputPath $patternInput -Output $patternOutput -ExpectedExitCode 0
if ($pattern['outcome'] -ne 'success' -or $pattern['inputBytes'] -ne $patternBytes.ToString() -or -not (Test-Path -LiteralPath $patternOutput)) { throw 'The pattern tile probe did not convert its tiled fill.' }
if (-not $pattern.ContainsKey('conversionManagedAllocatedBytes') -or [long]$pattern['conversionManagedAllocatedBytes'] -le 0) { throw 'The pattern tile probe did not report managed conversion allocation.' }

$patternDeniedOutput = Join-Path $ResultsDirectory "$runId-pattern-work-denied.png"
$patternDenied = Invoke-Probe -Name 'pattern-work-denied' -InputPath $patternInput -Output $patternDeniedOutput -ExpectedExitCode 1 -ExtraArguments @('--maximum-work-units', '10000')
if ($patternDenied['outcome'] -ne 'svg-error' -or $patternDenied['svgErrorCode'] -ne 'LimitExceeded' -or $patternDenied['inputBytes'] -ne $patternBytes.ToString() -or (Test-Path -LiteralPath $patternDeniedOutput)) { throw 'The work-denied pattern tile probe did not reject before output.' }

$xmlWorkspaceInput = Join-Path $ResultsDirectory "$runId-xml-workspace.svg"
$xmlWorkspacePayload = New-Object string('x', 64KB)
[IO.File]::WriteAllText($xmlWorkspaceInput, '<svg xmlns="http://www.w3.org/2000/svg"><metadata>' + $xmlWorkspacePayload + '</metadata></svg>', [Text.UTF8Encoding]::new($false))
$xmlWorkspaceBytes = [IO.FileInfo]::new($xmlWorkspaceInput).Length
$xmlWorkspaceBudget = [long]$xmlWorkspaceBytes * 4 - 1
$xmlWorkspaceOutput = Join-Path $ResultsDirectory "$runId-xml-workspace.png"
$xmlWorkspace = Invoke-Probe -Name 'xml-workspace' -InputPath $xmlWorkspaceInput -Output $xmlWorkspaceOutput -ExpectedExitCode 1 -ExtraArguments @('--maximum-accounted-bytes', $xmlWorkspaceBudget.ToString())
if ($xmlWorkspace['outcome'] -ne 'svg-error' -or $xmlWorkspace['svgErrorCode'] -ne 'LimitExceeded' -or $xmlWorkspace['inputBytes'] -ne $xmlWorkspaceBytes.ToString() -or (Test-Path -LiteralPath $xmlWorkspaceOutput)) { throw 'The XML workspace probe did not deny the input reservation before output.' }
Assert-XmlReaderMeasurements -Name 'xml-reader-text' -InputPath $xmlWorkspaceInput -MinimumTouchedCharacters 0 -MinimumTextNodes 1

$xmlAttributeInput = Join-Path $ResultsDirectory "$runId-xml-attributes.svg"
$xmlAttributes = (0..254 | ForEach-Object { " data-a$_=`"x`"" }) -join ''
[IO.File]::WriteAllText($xmlAttributeInput, '<svg xmlns="http://www.w3.org/2000/svg"' + $xmlAttributes + '/>', [Text.UTF8Encoding]::new($false))
$xmlAttributeBytes = [IO.FileInfo]::new($xmlAttributeInput).Length
$xmlAttributeOutput = Join-Path $ResultsDirectory "$runId-xml-attributes.png"
$xmlAttributesProbe = Invoke-Probe -Name 'xml-attributes' -InputPath $xmlAttributeInput -Output $xmlAttributeOutput -ExpectedExitCode 1 -ExtraArguments @('--maximum-accounted-bytes', '131071')
if ($xmlAttributesProbe['outcome'] -ne 'svg-error' -or $xmlAttributesProbe['svgErrorCode'] -ne 'LimitExceeded' -or $xmlAttributesProbe['inputBytes'] -ne $xmlAttributeBytes.ToString() -or (Test-Path -LiteralPath $xmlAttributeOutput)) { throw 'The XML attribute workspace probe did not deny the input reservation before output.' }

Assert-XmlReaderMeasurements -Name 'xml-reader' -InputPath $xmlAttributeInput -MinimumTouchedCharacters 1

$xmlReaderAttributeValueInput = Join-Path $ResultsDirectory "$runId-xml-reader-attribute-values.svg"
$xmlReaderAttributeValue = New-Object string('x', 4KB)
$xmlReaderAttributeValues = (0..254 | ForEach-Object { " data-a$_=`"$xmlReaderAttributeValue`"" }) -join ''
[IO.File]::WriteAllText($xmlReaderAttributeValueInput, '<svg xmlns="http://www.w3.org/2000/svg"' + $xmlReaderAttributeValues + '/>', [Text.UTF8Encoding]::new($false))
Assert-XmlReaderMeasurements -Name 'xml-reader-attribute-values' -InputPath $xmlReaderAttributeValueInput -MinimumTouchedCharacters ($xmlReaderAttributeValue.Length * 255)

$cancellationInput = Join-Path $ResultsDirectory "$runId-cancellation.svg"
[IO.File]::WriteAllText($cancellationInput, '<svg xmlns="http://www.w3.org/2000/svg" width="4096" height="4096"><rect width="4096" height="4096" fill="red"/></svg>', [Text.UTF8Encoding]::new($false))
$cancellationOutput = Join-Path $ResultsDirectory "$runId-cancellation.png"
$cancelled = Invoke-Probe -Name 'cancellation' -InputPath $cancellationInput -Output $cancellationOutput -ExpectedExitCode 3 -ExtraArguments @('--cancel-after-ms', '1')
if ($cancelled['outcome'] -ne 'cancelled' -or (Test-Path -LiteralPath $cancellationOutput)) { throw 'The cancellation probe completed or wrote output.' }

$parserCancellationInput = Join-Path $ResultsDirectory "$runId-parser-cancellation.svg"
$stylesheetPayload = New-Object string('x', 4MB)
[IO.File]::WriteAllText($parserCancellationInput, '<svg xmlns="http://www.w3.org/2000/svg"><style>/*' + $stylesheetPayload + '*/ rect { fill: red; }</style><rect width="1" height="1"/></svg>', [Text.UTF8Encoding]::new($false))
$parserCancellationOutput = Join-Path $ResultsDirectory "$runId-parser-cancellation.png"
$parserCancelled = Invoke-Probe -Name 'parser-cancellation' -InputPath $parserCancellationInput -Output $parserCancellationOutput -ExpectedExitCode 3 -ExtraArguments @('--cancel-after-conversion-start-ms', '1')
if ($parserCancelled['outcome'] -ne 'cancelled' -or $parserCancelled['cancellationMode'] -ne 'after-conversion-start' -or [int]$parserCancelled['inputBytes'] -lt 4MB -or (Test-Path -LiteralPath $parserCancellationOutput)) { throw 'The parser-stage cancellation probe completed, started too early, or wrote output.' }

$backgroundCancellationInput = Join-Path $ResultsDirectory "$runId-background-cancellation.svg"
[IO.File]::WriteAllText($backgroundCancellationInput, '<svg xmlns="http://www.w3.org/2000/svg" width="4096" height="4096"/>', [Text.UTF8Encoding]::new($false))
$backgroundCancellationOutput = Join-Path $ResultsDirectory "$runId-background-cancellation.png"
$backgroundCancelled = Invoke-Probe -Name 'background-cancellation' -InputPath $backgroundCancellationInput -Output $backgroundCancellationOutput -ExpectedExitCode 3 -ExtraArguments @('--background-color', '1,2,3,255', '--cancel-after-background-start-ms', '0')
if ($backgroundCancelled['outcome'] -ne 'cancelled' -or $backgroundCancelled['cancellationMode'] -ne 'after-background-start' -or $backgroundCancelled['backgroundInitializationStarted'] -ne 'true' -or $backgroundCancelled['backgroundCancellationObserved'] -ne 'true' -or [long]$backgroundCancelled['conversionManagedAllocatedBytes'] -lt 64MB -or (Test-Path -LiteralPath $backgroundCancellationOutput)) { throw 'The opaque-background cancellation probe did not observe cancellation in the initialized background loop.' }

$discoveryDeepGroupsInput = Join-Path $ResultsDirectory "$runId-font-discovery-deep-groups.svg"
$deepGroupsInner = ("<g>" * 64) + '<rect width="1" height="1"/>' + ("</g>" * 64)
[IO.File]::WriteAllText($discoveryDeepGroupsInput, '<svg xmlns="http://www.w3.org/2000/svg">' + $deepGroupsInner + "</svg>", [Text.UTF8Encoding]::new($false))
$discoveryDeepGroupsOutput = Join-Path $ResultsDirectory "$runId-font-discovery-deep-groups.png"
$deepGroups = Invoke-Probe -Name 'font-discovery-deep-groups' -InputPath $discoveryDeepGroupsInput -Output $discoveryDeepGroupsOutput -ExpectedExitCode 0 -ExtraArguments @('--mode', 'font-discovery')
if ($deepGroups['outcome'] -ne 'success' -or $deepGroups['discoveryRequestCount'] -ne '0' -or $deepGroups['providerCalls'] -ne '0' -or $deepGroups['discoveryCurrentBytes'] -ne '0' -or [long]$deepGroups['discoveryPeakBytes'] -le 0 -or $deepGroups['discoveryDeniedReservations'] -ne '0' -or (Test-Path -LiteralPath $discoveryDeepGroupsOutput)) { throw 'The deep-group font-discovery probe did not report an empty request set with a released ledger and no output.' }

$discoveryDeniedOutput = Join-Path $ResultsDirectory "$runId-font-discovery-deep-groups-denied.png"
$deepGroupsDenied = Invoke-Probe -Name 'font-discovery-deep-groups-denied' -InputPath $discoveryDeepGroupsInput -Output $discoveryDeniedOutput -ExpectedExitCode 1 -ExtraArguments @('--mode', 'font-discovery', '--maximum-accounted-bytes', '600')
if ($deepGroupsDenied['outcome'] -ne 'svg-error' -or $deepGroupsDenied['svgErrorCode'] -ne 'LimitExceeded' -or $deepGroupsDenied.ContainsKey('discoveryRequestCount') -or $deepGroupsDenied['providerCalls'] -ne '0' -or $deepGroupsDenied['discoveryCurrentBytes'] -ne '0' -or [long]$deepGroupsDenied['discoveryDeniedReservations'] -lt 1 -or (Test-Path -LiteralPath $discoveryDeniedOutput)) { throw 'The denied font-discovery probe did not fail with a ledger denial, no retained reservation, and no output.' }

$discoveryEmptyTextsInput = Join-Path $ResultsDirectory "$runId-font-discovery-empty-texts.svg"
$emptyTexts = (0..299 | ForEach-Object { "<text font-family=`"Empty$_`"/>" }) -join ''
[IO.File]::WriteAllText($discoveryEmptyTextsInput, '<svg xmlns="http://www.w3.org/2000/svg">' + $emptyTexts + "</svg>", [Text.UTF8Encoding]::new($false))
$discoveryEmptyTextsOutput = Join-Path $ResultsDirectory "$runId-font-discovery-empty-texts.png"
$emptyTextsProbe = Invoke-Probe -Name 'font-discovery-empty-texts' -InputPath $discoveryEmptyTextsInput -Output $discoveryEmptyTextsOutput -ExpectedExitCode 0 -ExtraArguments @('--mode', 'font-discovery')
if ($emptyTextsProbe['outcome'] -ne 'success' -or $emptyTextsProbe['discoveryRequestCount'] -ne '0' -or $emptyTextsProbe['providerCalls'] -ne '0' -or $emptyTextsProbe['discoveryCurrentBytes'] -ne '0' -or [long]$emptyTextsProbe['discoveryPeakBytes'] -le 0 -or $emptyTextsProbe['discoveryDeniedReservations'] -ne '0' -or (Test-Path -LiteralPath $discoveryEmptyTextsOutput)) { throw 'The empty-text font-discovery probe requested fonts, retained a reservation, or wrote output.' }

$discoveryDistinctInput = Join-Path $ResultsDirectory "$runId-font-discovery-distinct-requests.svg"
$distinctTexts = (0..9 | ForEach-Object { "<text font-family=`"Family$_`">x</text>" }) -join ''
[IO.File]::WriteAllText($discoveryDistinctInput, '<svg xmlns="http://www.w3.org/2000/svg">' + $distinctTexts + "</svg>", [Text.UTF8Encoding]::new($false))
$discoveryDistinctOutput = Join-Path $ResultsDirectory "$runId-font-discovery-distinct-requests.png"
$distinctProbe = Invoke-Probe -Name 'font-discovery-distinct-requests' -InputPath $discoveryDistinctInput -Output $discoveryDistinctOutput -ExpectedExitCode 1 -ExtraArguments @('--mode', 'font-discovery')
# Font-free discovery reports all requests. Coverage preparation parses each
# required program before further acquisition, so the first non-font rejects
# the operation without opening the nine remaining programs.
if ($distinctProbe['outcome'] -ne 'svg-error' -or $distinctProbe['svgErrorCode'] -ne 'UnsupportedFeature' -or $distinctProbe['discoveryRequestCount'] -ne '10' -or $distinctProbe['providerCalls'] -ne '1' -or $distinctProbe['discoveryCurrentBytes'] -ne '0' -or [long]$distinctProbe['discoveryPeakBytes'] -le 0 -or $distinctProbe['discoveryDeniedReservations'] -ne '0' -or (Test-Path -LiteralPath $discoveryDistinctOutput)) { throw 'The distinct-request font-discovery probe did not report every request, release its ledger, and reject the first non-font before further acquisition.' }

$discoveryCancelledInput = Join-Path $ResultsDirectory "$runId-font-discovery-cancelled.svg"
$cancelledStylesheet = New-Object string('x', 4MB)
[IO.File]::WriteAllText($discoveryCancelledInput, '<svg xmlns="http://www.w3.org/2000/svg"><style>/*' + $cancelledStylesheet + '*/ rect { fill: red; }</style><rect width="1" height="1"/></svg>', [Text.UTF8Encoding]::new($false))
$discoveryCancelledOutput = Join-Path $ResultsDirectory "$runId-font-discovery-cancelled.png"
$discoveryCancelled = Invoke-Probe -Name 'font-discovery-cancelled' -InputPath $discoveryCancelledInput -Output $discoveryCancelledOutput -ExpectedExitCode 3 -ExtraArguments @('--mode', 'font-discovery', '--cancel-after-ms', '1')
if ($discoveryCancelled['outcome'] -ne 'cancelled' -or $discoveryCancelled.ContainsKey('discoveryRequestCount') -or $discoveryCancelled['providerCalls'] -ne '0' -or $discoveryCancelled['discoveryCurrentBytes'] -ne '0' -or (Test-Path -LiteralPath $discoveryCancelledOutput)) { throw 'The cancelled font-discovery probe completed, acquired a program, retained a reservation, or wrote output.' }

$hostRuntimeFramework = [System.Runtime.InteropServices.RuntimeInformation]::FrameworkDescription
$dotnetRuntimes = @(& dotnet --list-runtimes 2>$null | ForEach-Object { if ($_ -match '^Microsoft\.NETCore\.App (\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?) ') { $Matches[1] } } | Sort-Object -Unique)
if ($dotnetRuntimes.Count -eq 0) { throw 'The dotnet host reported no .NETCore runtimes.' }
$fingerprints = @($reports | ForEach-Object { $_.report.operatingSystem + '|' + $_.report.processArchitecture + '|' + $_.report.runtimeFramework } | Sort-Object -Unique)
if ($fingerprints.Count -ne 1) { throw 'The child-process reports do not share one runtime/platform fingerprint.' }
$runtime = $reports[0].report
$summary = [ordered]@{
    schemaVersion = 3
    runId = $runId
    fixtureSha256 = (Get-FileHash -LiteralPath $fixture -Algorithm SHA256).Hash.ToLowerInvariant()
    runtimeFingerprint = [ordered]@{
        operatingSystem = $runtime.operatingSystem
        processArchitecture = $runtime.processArchitecture
        runtimeFramework = $runtime.runtimeFramework
    }
    hostRuntimeFramework = $hostRuntimeFramework
    dotnetRuntimes = @($dotnetRuntimes)
    reports = @($reports)
}
$summaryPath = Join-Path $ResultsDirectory "$runId-summary.json"
[IO.File]::WriteAllText($summaryPath, ($summary | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))

Write-Output "Memory probe scenarios passed, including $XmlReaderProbeRuns fresh-process XML-reader measurements for text and attribute shapes, fresh-process font-discovery ledger scenarios, two fresh-process embedded-image conversion scenarios, and two fresh-process pattern tile scenarios. Summary retained at $summaryPath."
