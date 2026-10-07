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
& (Join-Path $PSScriptRoot 'capture-chrome-process.ps1') -ChromeExecutable $executable -InputFile $inputFile -OutputFile $outputFile -Width $Width -Height $Height -Background $Background -CaptureTimeoutMilliseconds $CaptureTimeoutMilliseconds
