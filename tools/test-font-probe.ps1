param(
    [Parameter(Mandatory = $true)]
    [string] $ResultsDirectory,
    [int] $TimeoutMilliseconds = 120000
)

$ErrorActionPreference = 'Stop'

if ($TimeoutMilliseconds -le 0) { throw 'TimeoutMilliseconds must be positive.' }

$probeTool = Join-Path $PSScriptRoot 'Lokad.Svg.FontProbe/bin/Release/net10.0/Lokad.Svg.FontProbe.dll'
if (-not (Test-Path -LiteralPath $probeTool)) { throw "Font probe is not built: $probeTool" }

$runDirectory = Join-Path $ResultsDirectory ([Guid]::NewGuid().ToString('N'))
$fontRoot = Join-Path $runDirectory 'fonts'
$fontPath = Join-Path $fontRoot 'synthetic.ttf'
$manifestPath = Join-Path $runDirectory 'manifest.json'
$textSvgPath = Join-Path $runDirectory 'text.svg'
$emptySvgPath = Join-Path $runDirectory 'empty.svg'
$invalidOutput = Join-Path $runDirectory 'invalid.png'
$successOutput = Join-Path $runDirectory 'success.png'

New-Item -ItemType Directory -Force -Path $fontRoot | Out-Null
[IO.File]::WriteAllBytes($fontPath, [byte[]] @(0))
$hash = (Get-FileHash -LiteralPath $fontPath -Algorithm SHA256).Hash
[IO.File]::WriteAllText($manifestPath, "{ `"packId`": `"synthetic-font-probe`", `"files`": [{ `"relativePath`": `"synthetic.ttf`", `"byteSize`": 1, `"sha256`": `"$hash`" }], `"families`": [{ `"requestedFamily`": `"Synthetic`", `"resolvedFamily`": `"Synthetic`", `"relativeFontFile`": `"synthetic.ttf`" }] }", [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText($textSvgPath, "<svg xmlns=`"http://www.w3.org/2000/svg`" width=`"1`" height=`"1`"><text font-family=`"Synthetic`">A</text></svg>", [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText($emptySvgPath, "<svg xmlns=`"http://www.w3.org/2000/svg`" width=`"1`" height=`"1`"/>", [Text.UTF8Encoding]::new($false))

function Invoke-FontProbe {
    param(
        [Parameter(Mandatory = $true)] [string] $SvgPath,
        [Parameter(Mandatory = $true)] [string] $OutputPath,
        [Parameter(Mandatory = $true)] [int] $ExpectedExitCode,
        [byte[]] $StandardInput = [byte[]] @()
    )

    $start = [System.Diagnostics.ProcessStartInfo]::new('dotnet')
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.RedirectStandardInput = $StandardInput.Length -ne 0
    foreach ($argument in @($probeTool, '--manifest', $manifestPath, '--pack-id', 'synthetic-font-probe', '--font-root', $fontRoot, '--svg', $SvgPath, '--output', $OutputPath)) { $start.ArgumentList.Add($argument) }

    $process = [System.Diagnostics.Process]::Start($start)
    if ($null -eq $process) { throw 'Could not start the font probe.' }
    try {
        $standardOutputTask = $process.StandardOutput.ReadToEndAsync()
        $standardErrorTask = $process.StandardError.ReadToEndAsync()
        if ($StandardInput.Length -ne 0)
        {
            $process.StandardInput.BaseStream.Write($StandardInput, 0, $StandardInput.Length)
            $process.StandardInput.Close()
        }
        if (-not $process.WaitForExit($TimeoutMilliseconds))
        {
            $process.Kill($true)
            throw 'The font probe exceeded its timeout and was terminated.'
        }
        $standardOutput = $standardOutputTask.GetAwaiter().GetResult().Trim().Replace("`r`n", "`n")
        $standardError = $standardErrorTask.GetAwaiter().GetResult().Trim()
        if ($process.ExitCode -ne $ExpectedExitCode) { throw "The font probe exited $($process.ExitCode), expected $ExpectedExitCode." }
        if (-not [string]::IsNullOrEmpty($standardError)) { throw 'The font probe wrote unexpected standard error.' }
        return $standardOutput
    }
    finally { $process.Dispose() }
}

$invalidReport = Invoke-FontProbe -SvgPath $textSvgPath -OutputPath $invalidOutput -ExpectedExitCode 1
if ($invalidReport -cne "outcome=svg-error`nsvgErrorCode=MalformedInput" -or (Test-Path -LiteralPath $invalidOutput))
{
    throw 'The font probe did not acquire and reject the selected malformed synthetic program.'
}

$successReport = Invoke-FontProbe -SvgPath $emptySvgPath -OutputPath $successOutput -ExpectedExitCode 0
if ($successReport -notmatch '^outcome=success\npngBytes=\d+\nrequestedFontPrograms=0$' -or -not (Test-Path -LiteralPath $successOutput))
{
    throw 'The font probe did not preserve lazy program acquisition for an empty SVG.'
}

$stdinOutput = Join-Path $runDirectory 'stdin-success.png'
$stdinPayload = [Text.UTF8Encoding]::new($false).GetBytes('<svg xmlns="http://www.w3.org/2000/svg" width="1" height="1"/>')
$stdinBytes = [byte[]](([byte[]] @(0xEF, 0xBB, 0xBF)) + $stdinPayload)
$stdinReport = Invoke-FontProbe -SvgPath '-' -OutputPath $stdinOutput -ExpectedExitCode 0 -StandardInput $stdinBytes
if ($stdinReport -notmatch '^outcome=success\npngBytes=\d+\nrequestedFontPrograms=0$' -or -not (Test-Path -LiteralPath $stdinOutput))
{
    throw 'The font probe did not preserve standard-input SVG bytes through conversion.'
}

$httpManifest = [Text.UTF8Encoding]::new($false).GetBytes('{"packId":"synthetic-http-pack","files":[{"relativePath":"synthetic.ttf","byteSize":1,"sha256":"0000000000000000000000000000000000000000000000000000000000000000"}],"families":[{"requestedFamily":"Synthetic","resolvedFamily":"Synthetic","relativeFontFile":"synthetic.ttf"}]}')
$portListener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$portListener.Start()
$port = ([Net.IPEndPoint]$portListener.LocalEndpoint).Port
$portListener.Stop()
$sourceRoot = "http://127.0.0.1:$port/"
$httpOutput = Join-Path $runDirectory 'http-success.png'
$listener = [Net.HttpListener]::new()
$listener.Prefixes.Add($sourceRoot)
$listener.Start()
try {
    $start = [System.Diagnostics.ProcessStartInfo]::new('dotnet')
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in @($probeTool, '--source-uri', $sourceRoot, '--pack-id', 'synthetic-http-pack', '--svg', $emptySvgPath, '--output', $httpOutput)) { $start.ArgumentList.Add($argument) }
    $process = [System.Diagnostics.Process]::Start($start)
    if ($null -eq $process) { throw 'Could not start the HTTP font probe.' }
    try {
        $standardOutputTask = $process.StandardOutput.ReadToEndAsync()
        $standardErrorTask = $process.StandardError.ReadToEndAsync()
        $contextTask = $listener.GetContextAsync()
        if (-not $contextTask.Wait($TimeoutMilliseconds))
        {
            $process.Kill($true)
            throw 'The HTTP font probe did not request its manifest before the timeout.'
        }
        $context = $contextTask.GetAwaiter().GetResult()
        try {
            if ($context.Request.RawUrl -ne '/synthetic-http-pack/manifest.json')
            {
                $context.Response.StatusCode = 404
            }
            else
            {
                $context.Response.StatusCode = 200
                $context.Response.ContentLength64 = $httpManifest.Length
                $context.Response.OutputStream.Write($httpManifest, 0, $httpManifest.Length)
            }
        }
        finally { $context.Response.Close() }
        if (-not $process.WaitForExit($TimeoutMilliseconds))
        {
            $process.Kill($true)
            throw 'The HTTP font probe exceeded its timeout and was terminated.'
        }
        $standardOutput = $standardOutputTask.GetAwaiter().GetResult().Trim().Replace("`r`n", "`n")
        $standardError = $standardErrorTask.GetAwaiter().GetResult().Trim()
        if ($process.ExitCode -ne 0 -or $standardOutput -notmatch '^outcome=success\npngBytes=\d+\nrequestedFontPrograms=0$' -or -not [string]::IsNullOrEmpty($standardError) -or -not (Test-Path -LiteralPath $httpOutput))
        {
            throw 'The HTTP font probe did not complete the expected manifest-only conversion.'
        }
    }
    finally { $process.Dispose() }
}
finally {
    $listener.Stop()
    $listener.Close()
}

$retryManifest = [Text.UTF8Encoding]::new($false).GetBytes("{ `"packId`": `"synthetic-http-retry-pack`", `"files`": [{ `"relativePath`": `"synthetic.ttf`", `"byteSize`": 1, `"sha256`": `"$hash`" }], `"families`": [{ `"requestedFamily`": `"Synthetic`", `"resolvedFamily`": `"Synthetic`", `"relativeFontFile`": `"synthetic.ttf`" }] }")
$portListener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$portListener.Start()
$port = ([Net.IPEndPoint]$portListener.LocalEndpoint).Port
$portListener.Stop()
$sourceRoot = "http://127.0.0.1:$port/"
$retryOutput = Join-Path $runDirectory 'http-retry-invalid.png'
$listener = [Net.HttpListener]::new()
$listener.Prefixes.Add($sourceRoot)
$listener.Start()
try {
    $start = [System.Diagnostics.ProcessStartInfo]::new('dotnet')
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in @($probeTool, '--source-uri', $sourceRoot, '--pack-id', 'synthetic-http-retry-pack', '--svg', $textSvgPath, '--output', $retryOutput)) { $start.ArgumentList.Add($argument) }
    $process = [System.Diagnostics.Process]::Start($start)
    if ($null -eq $process) { throw 'Could not start the HTTP retry font probe.' }
    try {
        $standardOutputTask = $process.StandardOutput.ReadToEndAsync()
        $standardErrorTask = $process.StandardError.ReadToEndAsync()
        foreach ($request in 0..2)
        {
            $contextTask = $listener.GetContextAsync()
            if (-not $contextTask.Wait($TimeoutMilliseconds))
            {
                $process.Kill($true)
                throw 'The HTTP retry font probe did not complete its expected requests before the timeout.'
            }
            $context = $contextTask.GetAwaiter().GetResult()
            if ($request -eq 0)
            {
                if ($context.Request.RawUrl -ne '/synthetic-http-retry-pack/manifest.json') { $context.Response.StatusCode = 404; $context.Response.Close(); throw 'The HTTP retry font probe requested an unexpected manifest path.' }
                $context.Response.StatusCode = 200
                $context.Response.ContentLength64 = $retryManifest.Length
                $context.Response.OutputStream.Write($retryManifest, 0, $retryManifest.Length)
                $context.Response.Close()
            }
            elseif ($request -eq 1)
            {
                if ($context.Request.RawUrl -ne '/synthetic-http-retry-pack/synthetic.ttf') { $context.Response.StatusCode = 404; $context.Response.Close(); throw 'The HTTP retry font probe requested an unexpected program path.' }
                $context.Response.Abort()
            }
            else
            {
                if ($context.Request.RawUrl -ne '/synthetic-http-retry-pack/synthetic.ttf') { $context.Response.StatusCode = 404; $context.Response.Close(); throw 'The HTTP retry font probe did not retry the selected program path.' }
                $context.Response.StatusCode = 200
                $context.Response.ContentLength64 = 1
                $context.Response.OutputStream.WriteByte(0)
                $context.Response.Close()
            }
        }
        if (-not $process.WaitForExit($TimeoutMilliseconds))
        {
            $process.Kill($true)
            throw 'The HTTP retry font probe exceeded its timeout and was terminated.'
        }
        $standardOutput = $standardOutputTask.GetAwaiter().GetResult().Trim().Replace("`r`n", "`n")
        $standardError = $standardErrorTask.GetAwaiter().GetResult().Trim()
        if ($process.ExitCode -ne 1 -or $standardOutput -cne "outcome=svg-error`nsvgErrorCode=MalformedInput" -or -not [string]::IsNullOrEmpty($standardError) -or (Test-Path -LiteralPath $retryOutput))
        {
            throw 'The HTTP font probe did not retry and reject the selected malformed synthetic program.'
        }
    }
    finally { $process.Dispose() }
}
finally {
    $listener.Stop()
    $listener.Close()
}

Write-Output "Font probe synthetic checks passed: $runDirectory"
