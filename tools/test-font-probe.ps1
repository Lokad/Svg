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
        [Parameter(Mandatory = $true)] [int] $ExpectedExitCode
    )

    $start = [System.Diagnostics.ProcessStartInfo]::new('dotnet')
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in @($probeTool, '--manifest', $manifestPath, '--pack-id', 'synthetic-font-probe', '--font-root', $fontRoot, '--svg', $SvgPath, '--output', $OutputPath)) { $start.ArgumentList.Add($argument) }

    $process = [System.Diagnostics.Process]::Start($start)
    if ($null -eq $process) { throw 'Could not start the font probe.' }
    try {
        $standardOutputTask = $process.StandardOutput.ReadToEndAsync()
        $standardErrorTask = $process.StandardError.ReadToEndAsync()
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

Write-Output "Font probe synthetic checks passed: $runDirectory"
