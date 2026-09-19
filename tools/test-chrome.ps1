param(
    [Parameter(Mandatory = $true)]
    [string] $CacheDirectory,
    [Parameter(Mandatory = $true)]
    [string] $ResultsDirectory,
    [string] $PinPath = (Join-Path $PSScriptRoot 'chrome-for-testing.json')
)

$ErrorActionPreference = 'Stop'

$executable = & (Join-Path $PSScriptRoot 'setup-chrome.ps1') -CacheDirectory $CacheDirectory -PinPath $PinPath
$chromeTool = Join-Path $PSScriptRoot 'Lokad.Svg.Chrome/bin/Release/net10.0/Lokad.Svg.Chrome.dll'
$diffTool = Join-Path $PSScriptRoot 'Lokad.Svg.VisualDiff/bin/Release/net10.0/Lokad.Svg.VisualDiff.dll'
$fixture = Join-Path $PSScriptRoot '../tests/Fixtures/original/solid-red.svg'
$reference = Join-Path $PSScriptRoot '../tests/References/original-solid-red.png'
if (-not (Test-Path -LiteralPath $chromeTool)) { throw "Chrome tool is not built: $chromeTool" }
if (-not (Test-Path -LiteralPath $diffTool)) { throw "Visual-diff tool is not built: $diffTool" }
New-Item -ItemType Directory -Force -Path $ResultsDirectory | Out-Null
$candidate = Join-Path $ResultsDirectory ('pinned-chrome-smoke-' + [Guid]::NewGuid().ToString('N') + '.png')

$start = [System.Diagnostics.ProcessStartInfo]::new('dotnet')
$start.UseShellExecute = $false
$start.RedirectStandardInput = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$start.ArgumentList.Add($chromeTool)
$start.ArgumentList.Add($executable)
$start.ArgumentList.Add('2')
$start.ArgumentList.Add('2')
$process = [System.Diagnostics.Process]::Start($start)
if ($null -eq $process) { throw 'Chrome capture process could not be started.' }
try {
    $input = [System.IO.File]::OpenRead($fixture)
    try { $input.CopyTo($process.StandardInput.BaseStream) }
    finally { $input.Dispose(); $process.StandardInput.Close() }
    $output = [System.IO.File]::Create($candidate)
    try { $process.StandardOutput.BaseStream.CopyTo($output) }
    finally { $output.Dispose() }
    $standardError = $process.StandardError.ReadToEnd()
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw "Pinned Chrome capture failed: $standardError" }
}
finally { $process.Dispose() }

& dotnet $diffTool $reference $candidate
if ($LASTEXITCODE -ne 0) { throw 'Pinned Chrome smoke capture differs from its reference.' }
