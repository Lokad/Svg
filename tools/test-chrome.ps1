param(
    [Parameter(Mandatory = $true)]
    [string] $CacheDirectory,
    [Parameter(Mandatory = $true)]
    [string] $ResultsDirectory,
    [string] $PinPath = (Join-Path $PSScriptRoot 'chrome-for-testing.json'),
    [int] $CaptureRuns = 3
)

$ErrorActionPreference = 'Stop'
if ($CaptureRuns -le 0) { throw 'CaptureRuns must be positive.' }

$pin = Get-Content -LiteralPath $PinPath -Raw | ConvertFrom-Json
$setupChrome = Join-Path $PSScriptRoot 'setup-chrome.ps1'
$executable = & $setupChrome -CacheDirectory $CacheDirectory -PinPath $PinPath
$offlineExecutable = & $setupChrome -CacheDirectory $CacheDirectory -PinPath $PinPath -Offline
if ($offlineExecutable -ne $executable) { throw 'Offline Chrome provisioning resolved a different executable.' }
$executableVersion = (Get-Item -LiteralPath $executable).VersionInfo.ProductVersion
if (-not [string]::IsNullOrWhiteSpace($executableVersion) -and -not $executableVersion.Contains($pin.version, [StringComparison]::Ordinal)) { throw "Pinned Chrome executable version does not match its pin: $executableVersion" }
$chromeTool = Join-Path $PSScriptRoot 'Lokad.Svg.Chrome/bin/Release/net10.0/Lokad.Svg.Chrome.dll'
$diffTool = Join-Path $PSScriptRoot 'Lokad.Svg.VisualDiff/bin/Release/net10.0/Lokad.Svg.VisualDiff.dll'
$fixture = Join-Path $PSScriptRoot '../tests/Fixtures/original/solid-red.svg'
$reference = Join-Path $PSScriptRoot '../tests/References/original-solid-red.png'
if (-not (Test-Path -LiteralPath $chromeTool)) { throw "Chrome tool is not built: $chromeTool" }
if (-not (Test-Path -LiteralPath $diffTool)) { throw "Visual-diff tool is not built: $diffTool" }
New-Item -ItemType Directory -Force -Path $ResultsDirectory | Out-Null

function Invoke-ChromeCapture {
    param([Parameter(Mandatory = $true)] [string] $Candidate)

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
        $output = [System.IO.File]::Create($Candidate)
        try { $process.StandardOutput.BaseStream.CopyTo($output) }
        finally { $output.Dispose() }
        $standardError = $process.StandardError.ReadToEnd()
        $process.WaitForExit()
        if ($process.ExitCode -ne 0) { throw "Pinned Chrome capture failed: $standardError" }
    }
    finally { $process.Dispose() }
}

$captureId = [Guid]::NewGuid().ToString('N')
$candidates = [System.Collections.Generic.List[string]]::new()
$candidateHashes = [System.Collections.Generic.List[string]]::new()
for ($attempt = 1; $attempt -le $CaptureRuns; $attempt++)
{
    $candidate = Join-Path $ResultsDirectory "pinned-chrome-smoke-$captureId-$attempt.png"
    Invoke-ChromeCapture $candidate
    & dotnet $diffTool $reference $candidate
    if ($LASTEXITCODE -ne 0) { throw "Pinned Chrome smoke capture $attempt differs from its reference." }
    $candidateHash = (Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($candidateHashes.Count -gt 0 -and $candidateHash -ne $candidateHashes[0]) { throw "Pinned Chrome smoke capture $attempt is not byte-identical to the first fresh-process capture." }
    $candidates.Add($candidate)
    $candidateHashes.Add($candidateHash)
}

$fingerprint = [ordered]@{
    pinVersion = $pin.version
    chromiumRevision = $pin.chromiumRevision
    platform = $pin.platform
    executableSha256 = $pin.executableSha256
    executableFileVersion = $executableVersion
    captureRecipeRevision = $pin.captureRecipeRevision
    operatingSystem = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
    processArchitecture = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
    locale = [Globalization.CultureInfo]::CurrentCulture.Name
    headlessMode = 'new'
    gpuPolicy = 'disabled'
    sandbox = 'enabled'
    viewport = @{ width = 2; height = 2; deviceScaleFactor = 1 }
    background = 'transparent'
    freshProcessCaptures = $CaptureRuns
    candidatePngs = @($candidates | ForEach-Object { [IO.Path]::GetFileName($_) })
    candidateSha256 = @($candidateHashes)
}
$fingerprintPath = Join-Path $ResultsDirectory "pinned-chrome-smoke-$captureId.json"
$fingerprint | ConvertTo-Json | Set-Content -LiteralPath $fingerprintPath -NoNewline -Encoding utf8
