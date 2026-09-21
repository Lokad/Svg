param(
    [Parameter(Mandatory = $true)]
    [string] $ResultsDirectory,
    [ValidateSet('Windows', 'Linux')]
    [string] $Platform = 'Windows',
    [int] $TimeoutMilliseconds = 120000
)

$ErrorActionPreference = 'Stop'

if ($TimeoutMilliseconds -le 0) { throw 'TimeoutMilliseconds must be positive.' }

$validator = Join-Path $PSScriptRoot 'validate-ci-artifacts.ps1'
$comparisonTool = Join-Path $PSScriptRoot 'Lokad.Svg.VisualDiff/bin/Release/net10.0/Lokad.Svg.VisualDiff.dll'
$probeTool = Join-Path $PSScriptRoot 'Lokad.Svg.MemoryProbe/bin/Release/net10.0/Lokad.Svg.MemoryProbe.dll'
$fixture = Join-Path $PSScriptRoot '../tests/Fixtures/original/solid-red.svg'
$reference = Join-Path $PSScriptRoot '../tests/References/original-solid-red.png'
$pinPath = Join-Path $PSScriptRoot $(if ($Platform -eq 'Windows') { 'chrome-for-testing.json' } else { 'chrome-for-testing.linux64.json' })
$smokeScript = Join-Path $PSScriptRoot 'test-chrome.ps1'
$setupScript = Join-Path $PSScriptRoot 'setup-chrome.ps1'
$chromeTool = Join-Path $PSScriptRoot 'Lokad.Svg.Chrome/bin/Release/net10.0/Lokad.Svg.Chrome.dll'
foreach ($required in @($validator, $comparisonTool, $chromeTool, $probeTool, $fixture, $reference, $pinPath, $smokeScript, $setupScript)) {
    if (-not (Test-Path -LiteralPath $required)) { throw "Validator fixture dependency is missing: $required" }
}

function Get-TextSha256([string] $Path) {
    $text = [IO.File]::ReadAllText($Path).Replace("`r`n", "`n").Replace("`r", "`n")
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.UTF8Encoding]::new($false).GetBytes($text))).ToLowerInvariant()
}

function Invoke-Tool([string] $Tool, [string[]] $ToolArguments, [string] $Context) {
    $output = & dotnet $Tool @ToolArguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw "$Context failed with exit ${LASTEXITCODE}: $output" }
    return $output
}

function Write-Fingerprint([string] $Directory, [string] $RunId, [string[]] $CandidateNames, [string[]] $CandidateHashes) {
    $pin = Get-Content -LiteralPath $pinPath -Raw | ConvertFrom-Json
    $fingerprint = [ordered]@{
        schemaVersion = 2
        pinSha256 = (Get-FileHash -LiteralPath $pinPath -Algorithm SHA256).Hash.ToLowerInvariant()
        pinVersion = $pin.version
        chromiumRevision = $pin.chromiumRevision
        platform = $pin.platform
        archiveSha256 = $pin.archiveSha256
        executableSha256 = $pin.executableSha256
        captureRecipeRevision = $pin.captureRecipeRevision
        smokeScriptSha256 = Get-TextSha256 $smokeScript
        setupScriptSha256 = Get-TextSha256 $setupScript
        captureToolSha256 = (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'Lokad.Svg.Chrome/bin/Release/net10.0/Lokad.Svg.Chrome.dll') -Algorithm SHA256).Hash.ToLowerInvariant()
        comparisonToolSha256 = (Get-FileHash -LiteralPath $comparisonTool -Algorithm SHA256).Hash.ToLowerInvariant()
        fixtureSha256 = (Get-FileHash -LiteralPath $fixture -Algorithm SHA256).Hash.ToLowerInvariant()
        referenceSha256 = (Get-FileHash -LiteralPath $reference -Algorithm SHA256).Hash.ToLowerInvariant()
        operatingSystem = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
        processArchitecture = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
        runtimeFramework = [System.Runtime.InteropServices.RuntimeInformation]::FrameworkDescription
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
        freshProcessCaptures = 3
        captureTimeoutMilliseconds = 120000
        candidatePngs = @($CandidateNames)
        candidateSha256 = @($CandidateHashes)
    }
    $fingerprint | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Directory "pinned-chrome-smoke-$RunId.json") -NoNewline -Encoding utf8
}

function Invoke-Validator([string] $ChromeDirectory, [string] $MemoryDirectory) {
    return & $validator -Platform $Platform -ChromeDirectory $ChromeDirectory -MemoryDirectory $MemoryDirectory 2>&1
}

$runId = [Guid]::NewGuid().ToString('N')
$runDirectory = Join-Path $ResultsDirectory $runId
New-Item -ItemType Directory -Force -Path $runDirectory | Out-Null
$oursPng = Join-Path $runDirectory 'ours-solid-red.png'
Invoke-Tool $probeTool @($fixture, $oursPng) 'Reference rendering'
$variantSvg = Join-Path $runDirectory 'variant-blue.svg'
[IO.File]::WriteAllText($variantSvg, '<svg xmlns="http://www.w3.org/2000/svg" width="2" height="2"><rect width="2" height="2" fill="#0000ff"/></svg>', [Text.UTF8Encoding]::new($false))
$variantPng = Join-Path $runDirectory 'variant-blue.png'
Invoke-Tool $probeTool @($variantSvg, $variantPng) 'Variant rendering'
Invoke-Tool $comparisonTool @($reference, $oursPng) 'Positive fixture pixel gate'
try { Invoke-Tool $comparisonTool @($reference, $variantPng) 'Negative fixture pixel gate' | Out-Null; throw 'The pixel-mismatch fixture unexpectedly matches its reference.' }
catch { if ($_.Exception.Message -like 'The pixel-mismatch fixture*') { throw }; if ($LASTEXITCODE -eq 0) { throw 'The pixel comparison did not reject its mismatch fixture.' } }

$memoryDirectory = Join-Path $runDirectory 'memory'
& (Join-Path $PSScriptRoot 'test-memory-probe.ps1') -ResultsDirectory $memoryDirectory | Out-Null

$goodDirectory = Join-Path $runDirectory 'chrome-good'
New-Item -ItemType Directory -Force -Path $goodDirectory | Out-Null
$goodNames = @('candidate-1.png', 'candidate-2.png', 'candidate-3.png')
$goodHashes = @()
foreach ($name in $goodNames) {
    Copy-Item -LiteralPath $oursPng -Destination (Join-Path $goodDirectory $name)
    $goodHashes += (Get-FileHash -LiteralPath (Join-Path $goodDirectory $name) -Algorithm SHA256).Hash.ToLowerInvariant()
}
Write-Fingerprint $goodDirectory $runId $goodNames $goodHashes
Invoke-Validator $goodDirectory $memoryDirectory | Out-Null

$pixelDirectory = Join-Path $runDirectory 'chrome-pixel-mismatch'
New-Item -ItemType Directory -Force -Path $pixelDirectory | Out-Null
Copy-Item -LiteralPath $variantPng -Destination (Join-Path $pixelDirectory $goodNames[0])
Copy-Item -LiteralPath $oursPng -Destination (Join-Path $pixelDirectory $goodNames[1])
Copy-Item -LiteralPath $oursPng -Destination (Join-Path $pixelDirectory $goodNames[2])
$pixelHashes = @($goodNames | ForEach-Object { (Get-FileHash -LiteralPath (Join-Path $pixelDirectory $_) -Algorithm SHA256).Hash.ToLowerInvariant() })
Write-Fingerprint $pixelDirectory $runId $goodNames $pixelHashes
try { Invoke-Validator $pixelDirectory $memoryDirectory | Out-Null; throw 'The validator accepted a pixel-mismatching candidate.' }
catch { if ($_.Exception.Message -like 'The validator accepted*') { throw }; if ("$_" -notlike '*differs from its reference pixels*') { throw "The validator rejected the pixel mismatch for the wrong reason: $_" } }

$driftDirectory = Join-Path $runDirectory 'chrome-byte-drift'
New-Item -ItemType Directory -Force -Path $driftDirectory | Out-Null
Copy-Item -LiteralPath $oursPng -Destination (Join-Path $driftDirectory $goodNames[0])
Copy-Item -LiteralPath $oursPng -Destination (Join-Path $driftDirectory $goodNames[1])
Copy-Item -LiteralPath $reference -Destination (Join-Path $driftDirectory $goodNames[2])
$driftHashes = @($goodNames | ForEach-Object { (Get-FileHash -LiteralPath (Join-Path $driftDirectory $_) -Algorithm SHA256).Hash.ToLowerInvariant() })
if ($driftHashes[2] -ceq $driftHashes[0]) { throw 'The byte-drift fixture is not byte-distinct.' }
Write-Fingerprint $driftDirectory $runId $goodNames $driftHashes
try { Invoke-Validator $driftDirectory $memoryDirectory | Out-Null; throw 'The validator accepted byte-drifting candidates.' }
catch { if ($_.Exception.Message -like 'The validator accepted*') { throw }; if ("$_" -notlike '*not byte-identical to the first fresh-process capture*') { throw "The validator rejected the byte drift for the wrong reason: $_" } }

$tamperDirectory = Join-Path $runDirectory 'chrome-tampered-hash'
New-Item -ItemType Directory -Force -Path $tamperDirectory | Out-Null
foreach ($name in $goodNames) { Copy-Item -LiteralPath $oursPng -Destination (Join-Path $tamperDirectory $name) }
$tampered = $goodHashes[0].Substring(0, 63) + '0'
if ($tampered -ceq $goodHashes[0]) { $tampered = $goodHashes[0].Substring(0, 63) + '1' }
$tamperHashes = @($tampered, $goodHashes[1], $goodHashes[2])
Write-Fingerprint $tamperDirectory $runId $goodNames $tamperHashes
try { Invoke-Validator $tamperDirectory $memoryDirectory | Out-Null; throw 'The validator accepted a tampered recorded hash.' }
catch { if ($_.Exception.Message -like 'The validator accepted*') { throw }; if ("$_" -notlike '*file hash*') { throw "The validator rejected the tampered hash for the wrong reason: $_" } }

Write-Output "Validator artifact scenarios passed: $runDirectory"