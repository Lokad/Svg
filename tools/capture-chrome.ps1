param(
    [Parameter(Mandatory = $true)]
    [string] $InputPath,
    [Parameter(Mandatory = $true)]
    [string] $OutputPath,
    [Parameter(Mandatory = $true)]
    [int] $Width,
    [Parameter(Mandatory = $true)]
    [int] $Height,
    [Parameter(Mandatory = $true)]
    [string] $CacheDirectory,
    [string] $PinPath = (Join-Path $PSScriptRoot 'chrome-for-testing.json')
)

$ErrorActionPreference = 'Stop'

if ($Width -le 0 -or $Height -le 0) { throw 'Capture dimensions must be positive.' }
if (-not (Test-Path -LiteralPath $InputPath)) { throw "SVG input is missing: $InputPath" }

$executable = & (Join-Path $PSScriptRoot 'setup-chrome.ps1') -CacheDirectory $CacheDirectory -PinPath $PinPath
$chromeTool = Join-Path $PSScriptRoot 'Lokad.Svg.Chrome/bin/Release/net10.0/Lokad.Svg.Chrome.dll'
if (-not (Test-Path -LiteralPath $chromeTool)) { throw "Chrome tool is not built: $chromeTool" }

$outputDirectory = Split-Path -Parent $OutputPath
if (-not [string]::IsNullOrEmpty($outputDirectory)) { New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null }

$start = [System.Diagnostics.ProcessStartInfo]::new('dotnet')
$start.UseShellExecute = $false
$start.RedirectStandardInput = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$start.ArgumentList.Add($chromeTool)
$start.ArgumentList.Add($executable)
$start.ArgumentList.Add($Width.ToString([Globalization.CultureInfo]::InvariantCulture))
$start.ArgumentList.Add($Height.ToString([Globalization.CultureInfo]::InvariantCulture))
$process = [System.Diagnostics.Process]::Start($start)
if ($null -eq $process) { throw 'Chrome capture process could not be started.' }
try
{
    $input = [System.IO.File]::OpenRead($InputPath)
    try { $input.CopyTo($process.StandardInput.BaseStream) }
    finally { $input.Dispose(); $process.StandardInput.Close() }
    $output = [System.IO.File]::Create($OutputPath)
    try { $process.StandardOutput.BaseStream.CopyTo($output) }
    finally { $output.Dispose() }
    $standardError = $process.StandardError.ReadToEnd()
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw "Pinned Chrome capture failed: $standardError" }
}
finally { $process.Dispose() }
