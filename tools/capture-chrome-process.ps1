param(
    [Parameter(Mandatory = $true)] [string] $ChromeExecutable,
    [Parameter(Mandatory = $true)] [string] $InputFile,
    [Parameter(Mandatory = $true)] [string] $OutputFile,
    [Parameter(Mandatory = $true)] [int] $Width,
    [Parameter(Mandatory = $true)] [int] $Height,
    [Parameter(Mandatory = $true)] [string] $Background,
    [Parameter(Mandatory = $true)] [int] $CaptureTimeoutMilliseconds
)
# Internal process wrapper. The caller authenticates Chrome through setup-chrome.ps1;
# one batch can reuse that installation while every capture owns a fresh process/profile.
$ErrorActionPreference = 'Stop'
if ($Width -le 0 -or $Height -le 0 -or $CaptureTimeoutMilliseconds -le 0) { throw 'Capture dimensions and timeout must be positive.' }
if ($InputFile -eq $OutputFile) { throw 'Capture must not overwrite its SVG input.' }
$chromeTool = Join-Path $PSScriptRoot 'Lokad.Svg.Chrome/bin/Release/net10.0/Lokad.Svg.Chrome.dll'
if (-not (Test-Path -LiteralPath $chromeTool)) { throw "Chrome tool is not built: $chromeTool" }
New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($OutputFile)) | Out-Null
$staging = $OutputFile + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
$start = [Diagnostics.ProcessStartInfo]::new('dotnet')
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardInput = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$start.ArgumentList.Add($chromeTool)
$start.ArgumentList.Add($ChromeExecutable)
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
    $input = [IO.File]::OpenRead($InputFile)
    [void]$input.CopyToAsync($process.StandardInput.BaseStream).WaitAsync((Get-RemainingCaptureTime)).GetAwaiter().GetResult()
    $input.Dispose()
    $input = $null
    $process.StandardInput.Close()
    [void]$process.WaitForExitAsync().WaitAsync((Get-RemainingCaptureTime)).GetAwaiter().GetResult()
    [void]$outputTask.WaitAsync((Get-RemainingCaptureTime)).GetAwaiter().GetResult()
    $stderr = $stderrTask.WaitAsync((Get-RemainingCaptureTime)).GetAwaiter().GetResult()
    [IO.File]::WriteAllText(($OutputFile + '.stderr.txt'), $stderr)
    if ($process.ExitCode -ne 0) { throw "Pinned Chrome capture failed: $stderr" }
    $launch = ($stderr.Trim() | ConvertFrom-Json).chromeLaunch
    if ($null -eq $launch -or $launch.startupHealth -ne 'healthy' -or -not $launch.profileCleaned) { throw 'Chrome capture did not report healthy startup and cleanup.' }
    $output.Dispose()
    $output = $null
    [IO.File]::Move($staging, $OutputFile, $true)
}
finally {
    try {
        if (-not $process.HasExited) { $process.Kill($true) }
        if ($stderrTask.Wait(5000)) { [IO.File]::WriteAllText(($OutputFile + '.stderr.txt'), $stderrTask.GetAwaiter().GetResult()) }
    }
    finally {
        try {
            if ($null -ne $input) { $input.Dispose() }
            if ($null -ne $output) { $output.Dispose() }
        }
        finally {
            $process.Dispose()
            if ($createdStaging -and [IO.File]::Exists($staging)) { [IO.File]::Delete($staging) }
        }
    }
}
