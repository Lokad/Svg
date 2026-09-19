param(
    [Parameter(Mandatory = $true)]
    [string] $ResultsDirectory,
    [int] $TimeoutMilliseconds = 120000
)

$ErrorActionPreference = 'Stop'

if ($TimeoutMilliseconds -le 0) { throw 'TimeoutMilliseconds must be positive.' }

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

$successOutput = Join-Path $ResultsDirectory "$runId-success.png"
$success = Invoke-Probe -Name 'success' -InputPath $fixture -Output $successOutput -ExpectedExitCode 0 -ExtraArguments @('--maximum-accounted-bytes', '1048576')
if ($success['outcome'] -ne 'success' -or -not (Test-Path -LiteralPath $successOutput)) { throw 'The successful probe did not produce its expected PNG.' }

$limitOutput = Join-Path $ResultsDirectory "$runId-limit.png"
$limit = Invoke-Probe -Name 'limit' -InputPath $fixture -Output $limitOutput -ExpectedExitCode 1 -ExtraArguments @('--maximum-accounted-bytes', '1')
if ($limit['outcome'] -ne 'svg-error' -or $limit['svgErrorCode'] -ne 'LimitExceeded' -or (Test-Path -LiteralPath $limitOutput)) { throw 'The limited probe did not reject before output.' }

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
if ($parserCancelled['outcome'] -ne 'cancelled' -or (Test-Path -LiteralPath $parserCancellationOutput)) { throw 'The parser-stage cancellation probe completed or wrote output.' }

Write-Output "Memory probe scenarios passed. Reports retained in $ResultsDirectory."
