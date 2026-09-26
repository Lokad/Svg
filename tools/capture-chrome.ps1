param(
    [Parameter(Mandatory = $true)] [string] $InputPath,
    [Parameter(Mandatory = $true)] [string] $OutputPath,
    [Parameter(Mandatory = $true)] [int] $Width,
    [Parameter(Mandatory = $true)] [int] $Height,
    [Parameter(Mandatory = $true)] [string] $CacheDirectory,
    [ValidateSet('transparent', 'white', 'black')] [string] $Background = 'transparent',
    [string] $PinPath = (Join-Path $PSScriptRoot 'chrome-for-testing.json'),
    [int] $CaptureTimeoutMilliseconds = 120000
)
$ErrorActionPreference = 'Stop'
if ($Width -le 0 -or $Height -le 0) { throw 'Capture dimensions must be positive.' }
if ($CaptureTimeoutMilliseconds -le 0) { throw 'Capture timeout must be positive.' }
$inputFile = (Resolve-Path -LiteralPath $InputPath).Path
$outputFile = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputPath)
if ($inputFile -eq $outputFile) { throw 'Capture must not overwrite its SVG input.' }
$executable = & (Join-Path $PSScriptRoot 'setup-chrome.ps1') -CacheDirectory $CacheDirectory -PinPath $PinPath
$chromeTool = Join-Path $PSScriptRoot 'Lokad.Svg.Chrome/bin/Release/net10.0/Lokad.Svg.Chrome.dll'
if (-not (Test-Path -LiteralPath $chromeTool)) { throw "Chrome tool is not built: $chromeTool" }
New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($outputFile)) | Out-Null
$staging = $outputFile + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
$start = [Diagnostics.ProcessStartInfo]::new('dotnet')
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardInput = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$start.ArgumentList.Add($chromeTool)
$start.ArgumentList.Add($executable)
$start.ArgumentList.Add($Width.ToString([Globalization.CultureInfo]::InvariantCulture))
$start.ArgumentList.Add($Height.ToString([Globalization.CultureInfo]::InvariantCulture))
if ($Background -ne 'transparent') { $start.ArgumentList.Add($Background) }
$process = [Diagnostics.Process]::Start($start)
if ($null -eq $process) { throw 'Chrome capture process could not be started.' }
$clock = [Diagnostics.Stopwatch]::StartNew()
$stderrTask = $process.StandardError.ReadToEndAsync()
$output = $null
$input = $null
$createdStaging = $false
function Get-RemainingCaptureTime {
    $remaining = $CaptureTimeoutMilliseconds - $clock.ElapsedMilliseconds
    if ($remaining -le 0) { throw 'Chrome capture parent deadline expired.' }
    return [TimeSpan]::FromMilliseconds($remaining)
}
try {
    $output = [IO.File]::Open($staging, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    $createdStaging = $true
    $outputTask = $process.StandardOutput.BaseStream.CopyToAsync($output)
    $input = [IO.File]::OpenRead($inputFile)
    [void]$input.CopyToAsync($process.StandardInput.BaseStream).WaitAsync((Get-RemainingCaptureTime)).GetAwaiter().GetResult()
    $input.Dispose()
    $input = $null
    $process.StandardInput.Close()
    [void]$process.WaitForExitAsync().WaitAsync((Get-RemainingCaptureTime)).GetAwaiter().GetResult()
    [void]$outputTask.WaitAsync((Get-RemainingCaptureTime)).GetAwaiter().GetResult()
    $stderr = $stderrTask.WaitAsync((Get-RemainingCaptureTime)).GetAwaiter().GetResult()
    [IO.File]::WriteAllText(($outputFile + '.stderr.txt'), $stderr)
    if ($process.ExitCode -ne 0) { throw "Pinned Chrome capture failed: $stderr" }
    $launch = ($stderr.Trim() | ConvertFrom-Json).chromeLaunch
    if ($null -eq $launch -or $launch.startupHealth -ne 'healthy' -or -not $launch.profileCleaned) { throw 'Chrome capture did not report healthy startup and cleanup.' }
    $output.Dispose()
    $output = $null
    [IO.File]::Move($staging, $outputFile, $true)
}
finally {
    try {
        if (-not $process.HasExited) { $process.Kill($true) }
        if ($stderrTask.Wait(5000)) { [IO.File]::WriteAllText(($outputFile + '.stderr.txt'), $stderrTask.GetAwaiter().GetResult()) }
    }
    finally {
        try {
            if ($null -ne $input) { $input.Dispose() }
            if ($null -ne $output) { $output.Dispose() }
        }
        finally {
            $process.Dispose()
            # Only our uniquely created partial file is removed; an existing destination survives failures.
            if ($createdStaging -and [IO.File]::Exists($staging)) { [IO.File]::Delete($staging) }
        }
    }
}
