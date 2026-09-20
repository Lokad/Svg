param(
    [Parameter(Mandatory = $true)]
    [string] $ResultsDirectory,
    [int] $TimeoutMilliseconds = 120000,
    [int] $XmlReaderProbeRuns = 3
)

$ErrorActionPreference = 'Stop'

if ($TimeoutMilliseconds -le 0) { throw 'TimeoutMilliseconds must be positive.' }
if ($XmlReaderProbeRuns -le 0) { throw 'XmlReaderProbeRuns must be positive.' }

$probeTool = Join-Path $PSScriptRoot 'Lokad.Svg.MemoryProbe/bin/Release/net10.0/Lokad.Svg.MemoryProbe.dll'
$fixture = Join-Path $PSScriptRoot '../tests/Fixtures/original/solid-red.svg'
if (-not (Test-Path -LiteralPath $probeTool)) { throw "Memory probe is not built: $probeTool" }
if (-not (Test-Path -LiteralPath $fixture)) { throw "Probe fixture is missing: $fixture" }

New-Item -ItemType Directory -Force -Path $ResultsDirectory | Out-Null
$runId = [Guid]::NewGuid().ToString('N')

function Invoke-Probe {
    param(
        [Parameter(Mandatory = $true)] [string] $Name,
        [Parameter(Mandatory = $true)] [string] $InputPath,
        [Parameter(Mandatory = $true)] [string] $Output,
        [Parameter(Mandatory = $true)] [int] $ExpectedExitCode,
        [string[]] $ExtraArguments = @()
    )

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
        if (-not $values.ContainsKey('outcome') -or -not $values.ContainsKey('elapsedMilliseconds') -or -not $values.ContainsKey('peakWorkingSetBytes'))
        {
            throw "The $Name probe did not emit a complete report. See $ResultsDirectory for its output."
        }
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

Write-Output "Memory probe scenarios passed, including $XmlReaderProbeRuns fresh-process XML-reader measurements for text and attribute shapes. Reports retained in $ResultsDirectory."
