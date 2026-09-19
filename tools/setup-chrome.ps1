param(
    [Parameter(Mandatory = $true)]
    [string] $CacheDirectory
)

$pin = Get-Content (Join-Path $PSScriptRoot 'chrome-for-testing.json') -Raw | ConvertFrom-Json
$archive = Join-Path $CacheDirectory ('chrome-' + $pin.version + '-' + $pin.platform + '.zip')
$install = Join-Path $CacheDirectory ('chrome-' + $pin.version + '-' + $pin.platform)
New-Item -ItemType Directory -Force -Path $CacheDirectory | Out-Null
if (-not (Test-Path -LiteralPath $archive)) { Invoke-WebRequest $pin.archiveUrl -OutFile $archive }
$actual = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actual -ne $pin.archiveSha256) { throw "Chrome archive hash mismatch." }
$executable = Join-Path $install $pin.executableRelativePath
if (-not (Test-Path -LiteralPath $executable)) { Expand-Archive -LiteralPath $archive -DestinationPath $install -Force }
$executableActual = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash.ToLowerInvariant()
if ($executableActual -ne $pin.executableSha256) { throw "Chrome executable hash mismatch. Delete the cached installation and run setup again." }
Write-Output $executable
