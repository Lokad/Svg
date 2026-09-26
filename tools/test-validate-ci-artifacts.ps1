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

$chromeToolRuntime = @( & dotnet $chromeTool --runtime )
if ($LASTEXITCODE -ne 0 -or $chromeToolRuntime.Count -ne 1 -or [string]::IsNullOrWhiteSpace($chromeToolRuntime[0])) { throw 'The capture tool did not report its runtime.' }
$hostDotnetRuntimes = @(& dotnet --list-runtimes 2>$null | ForEach-Object { if ($_ -match '^Microsoft\.NETCore\.App (\d+\.\d+\.\d+) ') { $Matches[1] } } | Sort-Object -Unique)
if ($hostDotnetRuntimes.Count -eq 0) { throw 'The dotnet host reported no .NETCore runtimes.' }

function Write-Fingerprint([string] $Directory, [string] $RunId, [string[]] $CandidateNames, [string[]] $CandidateHashes) {
    $pin = Get-Content -LiteralPath $pinPath -Raw | ConvertFrom-Json
    $diagnostics = @()
    foreach ($candidateName in $CandidateNames) {
        $stderrName = $candidateName + '.stderr.txt'
        $stderrPath = Join-Path $Directory $stderrName
        @{ chromeLaunch = @{
            schemaVersion = 1; executableName = [IO.Path]::GetFileName($pin.executableRelativePath)
            elapsedMilliseconds = 20; debuggerReadyMilliseconds = 10; sandboxAccessFailures = 0
            networkServiceCrashes = 0; stderrTruncated = $false; stderr = ''; profileCleaned = $true; startupHealth = 'healthy'
        } } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $stderrPath -Encoding utf8
        $diagnostics += @{ stderrFile = $stderrName; stderrSha256 = (Get-FileHash -LiteralPath $stderrPath -Algorithm SHA256).Hash.ToLowerInvariant(); captureElapsedMilliseconds = 30 }
    }
    $fingerprint = [ordered]@{
        schemaVersion = 4
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
        hostRuntimeFramework = [System.Runtime.InteropServices.RuntimeInformation]::FrameworkDescription
        captureToolRuntimeFramework = [string]$chromeToolRuntime[0]
        dotnetRuntimes = @($hostDotnetRuntimes)
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
        captureDiagnostics = $diagnostics
    }
    $fingerprint | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $Directory "pinned-chrome-smoke-$RunId.json") -NoNewline -Encoding utf8
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

function Copy-MemoryReports([string] $Destination) {
    Copy-Item -Recurse -LiteralPath $memoryDirectory -Destination $Destination
    $summary = @(Get-ChildItem -LiteralPath $Destination -File -Filter '*-summary.json')
    if ($summary.Count -ne 1) { throw "Memory case does not contain exactly one summary: $Destination" }
    return $summary[0]
}

function Assert-ValidatorRejects([string] $ChromeDirectory, [string] $MemoryCase, [string] $Reason, [string] $Case) {
    try { Invoke-Validator $ChromeDirectory $MemoryCase | Out-Null; throw "The validator accepted $Case." }
    catch { if ($_.Exception.Message -like 'The validator accepted*') { throw }; if ("$_" -notlike $Reason) { throw "The validator rejected $Case for the wrong reason: $_" } }
}

$schemaMemory = Join-Path $runDirectory 'memory-bad-schema'
$schemaSummary = Copy-MemoryReports $schemaMemory
$schemaEdited = Get-Content -LiteralPath $schemaSummary.FullName -Raw | ConvertFrom-Json
$schemaEdited.schemaVersion = 99
$schemaEdited | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $schemaSummary.FullName -NoNewline -Encoding utf8
Assert-ValidatorRejects $goodDirectory $schemaMemory '*schema version*' 'a bad memory schema version'

$missingMemory = Join-Path $runDirectory 'memory-missing-report'
$missingSummary = Copy-MemoryReports $missingMemory
$missingEdited = Get-Content -LiteralPath $missingSummary.FullName -Raw | ConvertFrom-Json
$missingEdited.reports = @($missingEdited.reports | Where-Object { $_.name -ne 'limit' })
$missingEdited | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $missingSummary.FullName -NoNewline -Encoding utf8
Assert-ValidatorRejects $goodDirectory $missingMemory '*contains 24 reports*' 'a missing memory report'

$duplicateMemory = Join-Path $runDirectory 'memory-duplicate-report'
$duplicateSummary = Copy-MemoryReports $duplicateMemory
$duplicateEdited = Get-Content -LiteralPath $duplicateSummary.FullName -Raw | ConvertFrom-Json
$duplicateEdited.reports = @($duplicateEdited.reports) + @($duplicateEdited.reports[0])
$duplicateEdited | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $duplicateSummary.FullName -NoNewline -Encoding utf8
Assert-ValidatorRejects $goodDirectory $duplicateMemory '*contains 26 reports*' 'a duplicated memory report'

$unexpectedMemory = Join-Path $runDirectory 'memory-unexpected-report'
$unexpectedSummary = Copy-MemoryReports $unexpectedMemory
$unexpectedEdited = Get-Content -LiteralPath $unexpectedSummary.FullName -Raw | ConvertFrom-Json
$unexpectedEdited.reports[0].name = 'nope'
$unexpectedEdited | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $unexpectedSummary.FullName -NoNewline -Encoding utf8
Assert-ValidatorRejects $goodDirectory $unexpectedMemory '*unexpected or duplicate*' 'an unexpected memory report'

$hashMemory = Join-Path $runDirectory 'memory-tampered-input-hash'
$hashSummary = Copy-MemoryReports $hashMemory
$hashEdited = Get-Content -LiteralPath $hashSummary.FullName -Raw | ConvertFrom-Json
$successEntry = @($hashEdited.reports | Where-Object { $_.name -eq 'success' })[0]
$flipped = $successEntry.report.inputSha256.Substring(0, 63) + '0'
if ($flipped -ceq $successEntry.report.inputSha256) { $flipped = $successEntry.report.inputSha256.Substring(0, 63) + '1' }
$successEntry.report.inputSha256 = $flipped
$hashEdited | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $hashSummary.FullName -NoNewline -Encoding utf8
Assert-ValidatorRejects $goodDirectory $hashMemory '*input hash*' 'a tampered memory input hash'

$stdoutMemory = Join-Path $runDirectory 'memory-missing-stdout'
$stdoutSummary = Copy-MemoryReports $stdoutMemory
$stdoutEdited = Get-Content -LiteralPath $stdoutSummary.FullName -Raw | ConvertFrom-Json
Remove-Item -LiteralPath (Join-Path $stdoutMemory "$($stdoutEdited.runId)-success.stdout.txt")
Assert-ValidatorRejects $goodDirectory $stdoutMemory '*did not retain*' 'a missing memory transcript'

$outcomeMemory = Join-Path $runDirectory 'memory-flipped-outcome'
$outcomeSummary = Copy-MemoryReports $outcomeMemory
$outcomeEdited = Get-Content -LiteralPath $outcomeSummary.FullName -Raw | ConvertFrom-Json
@($outcomeEdited.reports | Where-Object { $_.name -eq 'success' })[0].report.outcome = 'svg-error'
$outcomeEdited | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $outcomeSummary.FullName -NoNewline -Encoding utf8
Assert-ValidatorRejects $goodDirectory $outcomeMemory '*outcome*' 'a flipped memory outcome'

$schemaChrome = Join-Path $runDirectory 'chrome-bad-schema'
New-Item -ItemType Directory -Force -Path $schemaChrome | Out-Null
foreach ($name in $goodNames) { Copy-Item -LiteralPath (Join-Path $goodDirectory $name) -Destination (Join-Path $schemaChrome $name) }
$chromeFingerprint = @(Get-ChildItem -LiteralPath $goodDirectory -File -Filter '*.json')[0]
$chromeEdited = Get-Content -LiteralPath $chromeFingerprint.FullName -Raw | ConvertFrom-Json
$chromeEdited.schemaVersion = 99
$chromeEdited | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $schemaChrome $chromeFingerprint.Name) -NoNewline -Encoding utf8
Assert-ValidatorRejects $schemaChrome $memoryDirectory '*schema version*' 'a bad chrome schema version'
function Copy-ChromeFingerprint([string] $Destination) {
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    foreach ($name in $goodNames) { Copy-Item -LiteralPath (Join-Path $goodDirectory $name) -Destination (Join-Path $Destination $name) }
    foreach ($name in $goodNames) { Copy-Item -LiteralPath (Join-Path $goodDirectory ($name + '.stderr.txt')) -Destination (Join-Path $Destination ($name + '.stderr.txt')) }
    $fingerprints = @(Get-ChildItem -LiteralPath $goodDirectory -File -Filter '*.json')
    if ($fingerprints.Count -ne 1) { throw "Good chrome directory does not contain exactly one fingerprint: $goodDirectory" }
    Copy-Item -LiteralPath $fingerprints[0].FullName -Destination (Join-Path $Destination $fingerprints[0].Name)
    return (Join-Path $Destination $fingerprints[0].Name)
}

$toolRuntimeChrome = Join-Path $runDirectory 'chrome-unknown-tool-runtime'
$toolRuntimeFingerprint = Copy-ChromeFingerprint $toolRuntimeChrome
$toolRuntimeEdited = Get-Content -LiteralPath $toolRuntimeFingerprint -Raw | ConvertFrom-Json
$toolRuntimeEdited.captureToolRuntimeFramework = '.NET 0.0.0'
$toolRuntimeEdited | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $toolRuntimeFingerprint -NoNewline -Encoding utf8
Assert-ValidatorRejects $toolRuntimeChrome $memoryDirectory '*not among the recorded dotnet runtimes*' 'an unlisted capture-tool runtime'

$chromeRuntimes = Join-Path $runDirectory 'chrome-missing-runtimes'
$chromeRuntimesFingerprint = Copy-ChromeFingerprint $chromeRuntimes
$chromeRuntimesEdited = Get-Content -LiteralPath $chromeRuntimesFingerprint -Raw | ConvertFrom-Json
$chromeRuntimesEdited.PSObject.Properties.Remove('dotnetRuntimes')
$chromeRuntimesEdited | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $chromeRuntimesFingerprint -NoNewline -Encoding utf8
Assert-ValidatorRejects $chromeRuntimes $memoryDirectory '*is missing dotnetRuntimes*' 'missing chrome dotnet runtimes'

$chromeHost = Join-Path $runDirectory 'chrome-host-mismatch'
$chromeHostFingerprint = Copy-ChromeFingerprint $chromeHost
$chromeHostEdited = Get-Content -LiteralPath $chromeHostFingerprint -Raw | ConvertFrom-Json
$chromeHostEdited.hostRuntimeFramework = '.NET 0.0.0'
$chromeHostEdited | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $chromeHostFingerprint -NoNewline -Encoding utf8
Assert-ValidatorRejects $chromeHost $memoryDirectory '*host runtime framework*' 'a mismatched chrome host runtime'
foreach ($case in @('sandbox-failure', 'network-crash', 'profile-leak', 'bad-timing', 'lying-stderr')) {
    $caseDirectory = Join-Path $runDirectory ('chrome-' + $case)
    $caseFingerprint = Copy-ChromeFingerprint $caseDirectory
    $edited = Get-Content -LiteralPath $caseFingerprint -Raw | ConvertFrom-Json
    $stderrPath = Join-Path $caseDirectory $edited.captureDiagnostics[0].stderrFile
    $stderr = Get-Content -LiteralPath $stderrPath -Raw | ConvertFrom-Json
    switch ($case) {
        'sandbox-failure' { $stderr.chromeLaunch.sandboxAccessFailures = 1; $reason = '*sandbox access failures*' }
        'network-crash' { $stderr.chromeLaunch.networkServiceCrashes = 2; $reason = '*network service repeatedly crashed*' }
        'profile-leak' { $stderr.chromeLaunch.profileCleaned = $false; $reason = '*profile cleanup*' }
        'bad-timing' { $stderr.chromeLaunch.debuggerReadyMilliseconds = 200; $reason = '*timings are inconsistent*' }
        'lying-stderr' { $stderr.chromeLaunch.stderr = 'Sandbox cannot access executable'; $reason = '*contradicts healthy startup*' }
    }
    $stderr | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $stderrPath -Encoding utf8
    $edited.captureDiagnostics[0].stderrSha256 = (Get-FileHash -LiteralPath $stderrPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $edited | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $caseFingerprint -Encoding utf8
    Assert-ValidatorRejects $caseDirectory $memoryDirectory $reason $case
}
$missingDiagnosticDirectory = Join-Path $runDirectory 'chrome-missing-diagnostics'
$missingDiagnosticFingerprint = Copy-ChromeFingerprint $missingDiagnosticDirectory
$missingDiagnostic = Get-Content -LiteralPath $missingDiagnosticFingerprint -Raw | ConvertFrom-Json
$missingDiagnostic.PSObject.Properties.Remove('captureDiagnostics')
$missingDiagnostic | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $missingDiagnosticFingerprint -Encoding utf8
Assert-ValidatorRejects $missingDiagnosticDirectory $memoryDirectory '*missing captureDiagnostics*' 'missing launch diagnostics'
function Copy-MemorySummary([string] $Destination) {
    Copy-Item -Recurse -LiteralPath $memoryDirectory -Destination $Destination
    $summaries = @(Get-ChildItem -LiteralPath $Destination -File -Filter '*-summary.json')
    if ($summaries.Count -ne 1) { throw "Memory case does not contain exactly one summary: $Destination" }
    return $summaries[0]
}

function Assert-MemoryRejects([string] $Case, [string] $Reason, [string] $CaseName, [scriptblock] $Mutate) {
    $caseDirectory = Join-Path $runDirectory $Case
    $summaryFile = Copy-MemorySummary $caseDirectory
    $edited = Get-Content -LiteralPath $summaryFile.FullName -Raw | ConvertFrom-Json
    & $Mutate $edited
    $edited | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $summaryFile.FullName -NoNewline -Encoding utf8
    Assert-ValidatorRejects $goodDirectory $caseDirectory $Reason $CaseName
}

Assert-MemoryRejects 'memory-unknown-child-runtime' '*not among the recorded dotnet runtimes*' 'an unlisted child runtime' {
    param($summary)
    $summary.runtimeFingerprint.runtimeFramework = '.NET 0.0.0'
}
Assert-MemoryRejects 'memory-missing-runtimes' '*is missing dotnetRuntimes*' 'missing dotnet runtimes' {
    param($summary)
    $summary.PSObject.Properties.Remove('dotnetRuntimes')
}
Assert-MemoryRejects 'memory-malformed-runtime' '*malformed runtime version*' 'a malformed runtime version' {
    param($summary)
    $summary.dotnetRuntimes = @('bogus')
}
Assert-MemoryRejects 'memory-missing-host' '*is missing hostRuntimeFramework*' 'a missing host runtime' {
    param($summary)
    $summary.PSObject.Properties.Remove('hostRuntimeFramework')
}
Assert-MemoryRejects 'memory-host-mismatch' '*host runtime framework*' 'a mismatched host runtime' {
    param($summary)
    $summary.hostRuntimeFramework = '.NET 0.0.0'
}
Write-Output "Validator artifact scenarios passed: $runDirectory"
