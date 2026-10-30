param(
    [Parameter(Mandatory = $true)]
    [string] $CacheDirectory,
    [Parameter(Mandatory = $true)]
    [string] $ResultsDirectory,
    [string] $PinPath = (Join-Path $PSScriptRoot 'chrome-for-testing.json'),
    [int] $CaptureRuns = 3,
    [int] $CaptureTimeoutMilliseconds = 120000,
    [ValidateSet('net11.0', 'net10.0')]
    [string] $TargetFramework = 'net11.0'
)

$ErrorActionPreference = 'Stop'
if ($CaptureRuns -ne 3) { throw 'Pinned Chrome smoke requires exactly three fresh-process captures.' }
if ($CaptureTimeoutMilliseconds -le 0) { throw 'CaptureTimeoutMilliseconds must be positive.' }

$pin = Get-Content -LiteralPath $PinPath -Raw | ConvertFrom-Json
$setupChrome = Join-Path $PSScriptRoot 'setup-chrome.ps1'
$executable = & $setupChrome -CacheDirectory $CacheDirectory -PinPath $PinPath
$offlineExecutable = & $setupChrome -CacheDirectory $CacheDirectory -PinPath $PinPath -Offline
if ($offlineExecutable -ne $executable) { throw 'Offline Chrome provisioning resolved a different executable.' }
$executableVersion = (Get-Item -LiteralPath $executable).VersionInfo.ProductVersion
if (-not [string]::IsNullOrWhiteSpace($executableVersion) -and -not $executableVersion.Contains($pin.version, [StringComparison]::Ordinal)) { throw "Pinned Chrome executable version does not match its pin: $executableVersion" }
$chromeTool = Join-Path $PSScriptRoot "Lokad.Svg.Chrome/bin/Release/$TargetFramework/Lokad.Svg.Chrome.dll"
$diffTool = Join-Path $PSScriptRoot "Lokad.Svg.VisualDiff/bin/Release/$TargetFramework/Lokad.Svg.VisualDiff.dll"
$fixture = Join-Path $PSScriptRoot '../tests/Fixtures/original/solid-red.svg'
$reference = Join-Path $PSScriptRoot '../tests/References/original-solid-red.png'
if (-not (Test-Path -LiteralPath $chromeTool)) { throw "Chrome tool is not built: $chromeTool" }
if (-not (Test-Path -LiteralPath $diffTool)) { throw "Visual-diff tool is not built: $diffTool" }
$captureToolRuntimeLines = @( & dotnet $chromeTool --runtime )
if ($LASTEXITCODE -ne 0 -or $captureToolRuntimeLines.Count -ne 1 -or [string]::IsNullOrWhiteSpace($captureToolRuntimeLines[0])) { throw 'The capture tool did not report its runtime.' }
$captureToolRuntime = [string]$captureToolRuntimeLines[0]
$dotnetRuntimes = @(& dotnet --list-runtimes 2>$null | ForEach-Object { if ($_ -match '^Microsoft\.NETCore\.App (\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?) ') { $Matches[1] } } | Sort-Object -Unique)
if ($dotnetRuntimes.Count -eq 0) { throw 'The dotnet host reported no .NETCore runtimes.' }
New-Item -ItemType Directory -Force -Path $ResultsDirectory | Out-Null

function Invoke-ChromeCapture {
    param(
        [Parameter(Mandatory = $true)] [string] $Candidate,
        [Parameter(Mandatory = $true)] [string] $SvgSource,
        [Parameter(Mandatory = $true)] [int] $TimeoutMilliseconds
    )

    $start = [System.Diagnostics.ProcessStartInfo]::new('dotnet')
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardInput = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    # Local DevTools HTTP/WebSocket traffic must work despite an unusable host proxy.
    foreach ($name in @('HTTP_PROXY', 'HTTPS_PROXY', 'ALL_PROXY')) { $start.Environment[$name] = 'http://127.0.0.1:1' }
    $start.Environment['NO_PROXY'] = ''
    $start.ArgumentList.Add($chromeTool)
    $start.ArgumentList.Add($executable)
    $start.ArgumentList.Add('2')
    $start.ArgumentList.Add('2')
    $process = [System.Diagnostics.Process]::Start($start)
    if ($null -eq $process) { throw 'Chrome capture process could not be started.' }
    $captureClock = [Diagnostics.Stopwatch]::StartNew()
    $standardErrorTask = $process.StandardError.ReadToEndAsync()
    try {
        $output = [System.IO.File]::Create($Candidate)
        try {
            $outputTask = $process.StandardOutput.BaseStream.CopyToAsync($output)
            $input = [System.IO.File]::OpenRead($SvgSource)
            try { $input.CopyTo($process.StandardInput.BaseStream) }
            finally { $input.Dispose(); $process.StandardInput.Close() }
            if (-not $process.WaitForExit($TimeoutMilliseconds))
            {
                $process.Kill($true)
                throw "Pinned Chrome capture exceeded $TimeoutMilliseconds ms and was terminated."
            }
            [void]$outputTask.GetAwaiter().GetResult()
            $standardError = $standardErrorTask.GetAwaiter().GetResult()
            $stderrPath = $Candidate + '.stderr.txt'
            [IO.File]::WriteAllText($stderrPath, $standardError)
            if ($process.ExitCode -ne 0) { throw "Pinned Chrome capture failed: $standardError" }
            $report = $standardError.Trim() | ConvertFrom-Json
            $launch = $report.chromeLaunch
            if ($null -eq $launch -or $launch.schemaVersion -ne 1 -or $launch.startupHealth -ne 'healthy' -or
                $launch.sandboxAccessFailures -ne 0 -or $launch.networkServiceCrashes -ge 2 -or -not $launch.profileCleaned) {
                throw 'Pinned Chrome capture did not report healthy sandbox startup and profile cleanup.'
            }
            return [PSCustomObject]@{
                stderrFile = [IO.Path]::GetFileName($stderrPath)
                captureElapsedMilliseconds = $captureClock.ElapsedMilliseconds
            }
        }
        finally { $output.Dispose() }
    }
    catch {
        if (-not $process.HasExited) { $process.Kill($true) }
        if ($standardErrorTask.Wait(5000)) { [IO.File]::WriteAllText(($Candidate + '.stderr.txt'), $standardErrorTask.GetAwaiter().GetResult()) }
        throw
    }
    finally { $process.Dispose() }
}

$captureId = [Guid]::NewGuid().ToString('N')
$candidates = [System.Collections.Generic.List[string]]::new()
$candidateHashes = [System.Collections.Generic.List[string]]::new()
$captureDiagnostics = [System.Collections.Generic.List[object]]::new()
$profilesBefore = [Collections.Generic.HashSet[string]]::new([string[]][IO.Directory]::GetDirectories([IO.Path]::GetTempPath(), 'lokad-svg-chrome-*'), [StringComparer]::Ordinal)
$captureRootsBefore = [Collections.Generic.HashSet[string]]::new([string[]][IO.Directory]::GetDirectories([IO.Path]::GetTempPath(), 'lokad-svg-capture-*'), [StringComparer]::Ordinal)
for ($attempt = 1; $attempt -le $CaptureRuns; $attempt++)
{
    $candidate = Join-Path $ResultsDirectory "pinned-chrome-smoke-$captureId-$attempt.png"
    $captureDiagnostics.Add((Invoke-ChromeCapture -Candidate $candidate -SvgSource $fixture -TimeoutMilliseconds $CaptureTimeoutMilliseconds))
    & dotnet $diffTool $reference $candidate
    if ($LASTEXITCODE -ne 0) { throw "Pinned Chrome smoke capture $attempt differs from its reference." }
    $candidateHash = (Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($candidateHashes.Count -gt 0 -and $candidateHash -ne $candidateHashes[0]) { throw "Pinned Chrome smoke capture $attempt is not byte-identical to the first fresh-process capture." }
    $candidates.Add($candidate)
    $candidateHashes.Add($candidateHash)
}

# An inert comment keeps the exact red pixels while exceeding Chromium's 2 MiB
# navigation URL once the SVG and HTML are base64-encoded. The capture tool must
# deliver the same image through its bounded Blob transport, with fresh profiles.
$largeSource = Join-Path $ResultsDirectory "large-svg-$captureId.svg"
$largeSvg = [IO.File]::ReadAllText($fixture).Replace('</svg>', ('<!--' + [string]::new('x', 1200000) + '--></svg>'), [StringComparison]::Ordinal)
[IO.File]::WriteAllText($largeSource, $largeSvg, [Text.UTF8Encoding]::new($false))
for ($attempt = 1; $attempt -le $CaptureRuns; $attempt++)
{
    $candidate = Join-Path $ResultsDirectory "large-svg-$captureId-$attempt.png"
    [void](Invoke-ChromeCapture -Candidate $candidate -SvgSource $largeSource -TimeoutMilliseconds $CaptureTimeoutMilliseconds)
    & dotnet $diffTool $reference $candidate
    if ($LASTEXITCODE -ne 0) { throw "Large SVG capture $attempt differs from its unchanged red reference." }
    if ((Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash.ToLowerInvariant() -cne $candidateHashes[0]) { throw 'Large SVG Blob capture changed the established image recipe pixels.' }
}

# A square viewBox in a 300x150 image starts its red content at x=75. A 16x16
# screenshot is therefore transparent. Without the named image viewport this
# same source fills the screenshot, so the control detects an ignored CLI option.
$viewportSource = Join-Path $ResultsDirectory "image-viewport-$captureId.svg"
$viewportSvg = [IO.File]::ReadAllText($fixture).Replace('width="2" height="2"', 'viewBox="0 0 2 2"', [StringComparison]::Ordinal)
if ($viewportSvg -ceq [IO.File]::ReadAllText($fixture)) { throw 'The viewport control no longer matches its source fixture.' }
[IO.File]::WriteAllText($viewportSource, $viewportSvg, [Text.UTF8Encoding]::new($false))
$viewportReference = Join-Path $PSScriptRoot '../tests/References/original-zero-path-stroke-16x16.png'
$viewportHashes = [Collections.Generic.List[string]]::new()
for ($attempt = 1; $attempt -le $CaptureRuns; $attempt++) {
    $candidate = Join-Path $ResultsDirectory "image-viewport-$captureId-$attempt.png"
    & (Join-Path $PSScriptRoot 'capture-chrome-process.ps1') -ChromeExecutable $executable -InputFile $viewportSource -OutputFile $candidate -Width 16 -Height 16 -Background transparent -CaptureTimeoutMilliseconds $CaptureTimeoutMilliseconds -TargetFramework $TargetFramework -ImageViewportWidth 300 -ImageViewportHeight 150
    & dotnet $diffTool $viewportReference $candidate
    if ($LASTEXITCODE -ne 0) { throw 'Explicit image viewport did not preserve its letterbox and screenshot crop.' }
    $hash = (Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($viewportHashes.Count -gt 0 -and $hash -cne $viewportHashes[0]) { throw 'Explicit image viewport captures differ across fresh processes.' }
    $viewportHashes.Add($hash)
}

$fingerprint = [ordered]@{
    schemaVersion = 4
    pinSha256 = (Get-FileHash -LiteralPath $PinPath -Algorithm SHA256).Hash.ToLowerInvariant()
    pinVersion = $pin.version
    chromiumRevision = $pin.chromiumRevision
    platform = $pin.platform
    archiveSha256 = $pin.archiveSha256
    executableSha256 = $pin.executableSha256
    executableFileVersion = $executableVersion
    captureRecipeRevision = $pin.captureRecipeRevision
    fixtureSha256 = (Get-FileHash -LiteralPath $fixture -Algorithm SHA256).Hash.ToLowerInvariant()
    referenceSha256 = (Get-FileHash -LiteralPath $reference -Algorithm SHA256).Hash.ToLowerInvariant()
    operatingSystem = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
    processArchitecture = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
    hostRuntimeFramework = [System.Runtime.InteropServices.RuntimeInformation]::FrameworkDescription
    captureToolRuntimeFramework = $captureToolRuntime
    dotnetRuntimes = @($dotnetRuntimes)
    locale = [Globalization.CultureInfo]::CurrentCulture.Name
    timeZone = [TimeZoneInfo]::Local.Id
    headlessMode = 'new'
    gpuPolicy = 'disabled'
    sandbox = 'enabled'
    resourcePolicy = 'data URLs only; http, https, file, ftp, ws and wss are blocked through DevTools Network.setBlockedURLs'
    colorSpacePolicy = 'browser-default PNG screenshot; no DevTools color-space override'
    fontPolicy = 'not-exercised: fixed smoke SVG has no text'
    viewport = @{ width = 2; height = 2; deviceScaleFactor = 1 }
    background = 'transparent'
    freshProcessCaptures = $CaptureRuns
    captureTimeoutMilliseconds = $CaptureTimeoutMilliseconds
    candidatePngs = @($candidates | ForEach-Object { [IO.Path]::GetFileName($_) })
    candidateSha256 = @($candidateHashes)
    captureDiagnostics = @($captureDiagnostics)
    imageViewportControl = @{
        sourceSha256 = (Get-FileHash -LiteralPath $viewportSource -Algorithm SHA256).Hash.ToLowerInvariant()
        referenceSha256 = (Get-FileHash -LiteralPath $viewportReference -Algorithm SHA256).Hash.ToLowerInvariant()
        imageViewport = @{ width = 300; height = 150 }
        screenshot = @{ width = 16; height = 16 }
        candidateSha256 = @($viewportHashes)
    }
}
$fingerprintPath = Join-Path $ResultsDirectory "pinned-chrome-smoke-$captureId.json"

# Exercise the interactive wrapper's failure/timeout publication contract as well.
$wrapperDirectory = Join-Path $ResultsDirectory ('wrapper-' + $captureId)
New-Item -ItemType Directory -Path $wrapperDirectory | Out-Null
$wrapperOutput = Join-Path $wrapperDirectory 'preserved.png'
Copy-Item -LiteralPath $reference -Destination $wrapperOutput
$preservedHash = (Get-FileHash -LiteralPath $wrapperOutput -Algorithm SHA256).Hash
$captureScript = Join-Path $PSScriptRoot 'capture-chrome.ps1'
try {
    & $captureScript -InputPath $fixture -OutputPath $wrapperOutput -Width 4096 -Height 2049 -CacheDirectory $CacheDirectory -PinPath $PinPath
    throw 'Wrapper unexpectedly accepted excessive dimensions.'
}
catch { if ($_.Exception.Message -notlike '*8,388,608-pixel transport limit*') { throw } }
try {
    & $captureScript -InputPath $fixture -OutputPath $wrapperOutput -Width 2 -Height 2 -CacheDirectory $CacheDirectory -PinPath $PinPath -CaptureTimeoutMilliseconds 1
    throw 'Wrapper unexpectedly completed inside the forced deadline.'
}
catch { if ($_.Exception.Message -notmatch 'deadline|timed out') { throw } }
if ((Get-FileHash -LiteralPath $wrapperOutput -Algorithm SHA256).Hash -cne $preservedHash) { throw 'Failed capture replaced the existing destination.' }
if (@(Get-ChildItem -LiteralPath $wrapperDirectory -Filter '*.tmp').Count -ne 0) { throw 'Failed capture left partial publication files.' }
if (-not $profilesBefore.SetEquals([string[]][IO.Directory]::GetDirectories([IO.Path]::GetTempPath(), 'lokad-svg-chrome-*'))) { throw 'Chrome smoke or wrapper controls leaked a profile; pre-existing profiles were not modified.' }
if (-not $captureRootsBefore.SetEquals([string[]][IO.Directory]::GetDirectories([IO.Path]::GetTempPath(), 'lokad-svg-capture-*'))) { throw 'Chrome wrapper controls leaked an owned temporary root; pre-existing roots were not modified.' }
$fingerprint | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $fingerprintPath -NoNewline -Encoding utf8
