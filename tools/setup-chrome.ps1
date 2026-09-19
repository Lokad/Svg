param(
    [Parameter(Mandatory = $true)]
    [string] $CacheDirectory,
    [string] $PinPath = (Join-Path $PSScriptRoot 'chrome-for-testing.json')
)

$pin = Get-Content -LiteralPath $PinPath -Raw | ConvertFrom-Json
$archive = Join-Path $CacheDirectory ('chrome-' + $pin.version + '-' + $pin.platform + '.zip')
$install = Join-Path $CacheDirectory ('chrome-' + $pin.version + '-' + $pin.platform)
New-Item -ItemType Directory -Force -Path $CacheDirectory | Out-Null
if (-not (Test-Path -LiteralPath $archive)) { Invoke-WebRequest $pin.archiveUrl -OutFile $archive }
$actual = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actual -ne $pin.archiveSha256) { throw "Chrome archive hash mismatch." }
$executable = Join-Path $install $pin.executableRelativePath
if (-not (Test-Path -LiteralPath $executable)) { Expand-Archive -LiteralPath $archive -DestinationPath $install -Force }
if ($env:OS -ne 'Windows_NT') { & chmod +x $executable; if ($LASTEXITCODE -ne 0) { throw "Chrome executable permissions could not be set." } }
$executableActual = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash.ToLowerInvariant()
if ($executableActual -ne $pin.executableSha256) { throw "Chrome executable hash mismatch. Delete the cached installation and run setup again." }
Write-Output $executable
