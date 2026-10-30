param(
    [Parameter(Mandatory = $true)] [string] $ChromeExecutable,
    [Parameter(Mandatory = $true)] [string] $InputFile,
    [Parameter(Mandatory = $true)] [string] $OutputFile,
    [Parameter(Mandatory = $true)] [int] $Width,
    [Parameter(Mandatory = $true)] [int] $Height,
    [Parameter(Mandatory = $true)] [string] $Background,
    [Parameter(Mandatory = $true)] [int] $CaptureTimeoutMilliseconds,
    [ValidateSet('net11.0', 'net10.0')]
    [string] $TargetFramework = 'net11.0',
    [Nullable[double]] $ImageViewportWidth,
    [Nullable[double]] $ImageViewportHeight
)
# Internal process wrapper. The caller authenticates Chrome through setup-chrome.ps1;
# one batch can reuse that installation while every capture owns a fresh process/profile.
$ErrorActionPreference = 'Stop'
if ($Width -le 0 -or $Height -le 0 -or $CaptureTimeoutMilliseconds -le 0) { throw 'Capture dimensions and timeout must be positive.' }
if (($null -eq $ImageViewportWidth) -ne ($null -eq $ImageViewportHeight)) { throw 'Supply both image viewport dimensions.' }
if ($null -ne $ImageViewportWidth -and (-not [double]::IsFinite($ImageViewportWidth) -or $ImageViewportWidth -le 0 -or $ImageViewportWidth -gt 16384 -or -not [double]::IsFinite($ImageViewportHeight) -or $ImageViewportHeight -le 0 -or $ImageViewportHeight -gt 16384)) { throw 'Image viewport dimensions must be finite, positive and within the capture side limit.' }
if ($InputFile -eq $OutputFile) { throw 'Capture must not overwrite its SVG input.' }
$chromeTool = Join-Path $PSScriptRoot "Lokad.Svg.Chrome/bin/Release/$TargetFramework/Lokad.Svg.Chrome.dll"
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
if ($null -ne $ImageViewportWidth) {
    $start.ArgumentList.Add('--image-viewport')
    $start.ArgumentList.Add($ImageViewportWidth.ToString('R', [Globalization.CultureInfo]::InvariantCulture))
    $start.ArgumentList.Add($ImageViewportHeight.ToString('R', [Globalization.CultureInfo]::InvariantCulture))
}
# The parent owns this private temporary root even when its child is forcibly
# terminated before ChromeProcess can run its profile cleanup.
$captureTempParent = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath([IO.Path]::GetTempPath()))
$captureTempDirectory = [IO.Path]::GetFullPath((Join-Path $captureTempParent ('lokad-svg-capture-' + [Guid]::NewGuid().ToString('N'))))
if ([IO.Path]::GetDirectoryName($captureTempDirectory) -cne $captureTempParent) { throw 'Capture temporary root escapes the intended temporary directory.' }
$start.Environment['TMPDIR'] = $captureTempDirectory
$start.Environment['TEMP'] = $captureTempDirectory
$start.Environment['TMP'] = $captureTempDirectory
$process = $null
$stderrTask = $null
$createdTempDirectory = $false
$output = $null
$input = $null
$createdStaging = $false
function Get-RemainingCaptureTime {
    $remaining = $CaptureTimeoutMilliseconds - $clock.ElapsedMilliseconds
    if ($remaining -le 0) { throw 'Chrome capture parent deadline expired.' }
    return [TimeSpan]::FromMilliseconds($remaining)
}
try {
    New-Item -ItemType Directory -Path $captureTempDirectory | Out-Null
    $createdTempDirectory = $true
    $process = [Diagnostics.Process]::Start($start)
    if ($null -eq $process) { throw 'Chrome capture process could not be started.' }
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $stderrTask = $process.StandardError.ReadToEndAsync()
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
        if ($null -ne $process) {
            if (-not $process.HasExited) { $process.Kill($true) }
            if (-not $process.WaitForExit(5000)) { throw 'Chrome capture process did not terminate after its parent deadline.' }
        }
        if ($null -ne $stderrTask -and $stderrTask.Wait(5000)) { [IO.File]::WriteAllText(($OutputFile + '.stderr.txt'), $stderrTask.GetAwaiter().GetResult()) }
    }
    finally {
        try {
            if ($null -ne $input) { $input.Dispose() }
            if ($null -ne $output) { $output.Dispose() }
        }
        finally {
            try {
                if ($null -ne $process) { $process.Dispose() }
                if ($createdStaging -and [IO.File]::Exists($staging)) { [IO.File]::Delete($staging) }
            }
            finally {
                if ($createdTempDirectory -and [IO.Directory]::Exists($captureTempDirectory)) {
                    Remove-Item -LiteralPath $captureTempDirectory -Recurse -Force
                }
            }
        }
    }
}
