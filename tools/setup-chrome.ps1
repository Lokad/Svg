param(
    [Parameter(Mandatory = $true)] [string] $CacheDirectory,
    [string] $PinPath = (Join-Path $PSScriptRoot 'chrome-for-testing.json'),
    [switch] $Offline
)

$ErrorActionPreference = 'Stop'

function Assert-ChromePath([string] $Parent, [string] $Child) {
    $prefix = [IO.Path]::GetFullPath($Parent).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $full = [IO.Path]::GetFullPath($Child)
    $comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    if (-not $full.StartsWith($prefix, $comparison)) { throw 'Chrome path must be strictly inside the selected cache/installation.' }
    # Do not follow a junction/symlink to a different permission target, including in ancestors.
    $ancestor = $full
    while (-not [string]::IsNullOrEmpty($ancestor)) {
        if (Test-Path -LiteralPath $ancestor) {
            if (((Get-Item -Force -LiteralPath $ancestor).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw 'Chrome cache/installation paths must not traverse a reparse point.'
            }
        }
        $ancestor = [IO.Path]::GetDirectoryName($ancestor)
    }
    return $full
}

function Assert-ChromeSetupExitCode([int] $ExitCode) {
    # Chromium InstallStatus CONFIGURE_APP_CONTAINER_SANDBOX_SUCCESS is 78.
    if ($ExitCode -notin @(0, 78)) {
        throw "Chrome sandbox permission setup failed (exit $ExitCode). Check write-DACL access to this pinned installation; do not disable the sandbox."
    }
}

function Assert-ChromeSandboxAccess([string] $BrowserDirectory) {
    # Named chromeInstallFiles / lpacChromeInstallFiles capabilities used by the pinned installer.
    $capabilities = @(
        'S-1-15-3-1024-3424233489-972189580-2057154623-747635277-1604371224-316187997-3786583170-1043257646',
        'S-1-15-3-1024-2302894289-466761758-1166120688-1039016420-2430351297-4240214049-4028510897-3317428798'
    )
    $required = [Security.AccessControl.FileSystemRights]::ReadAndExecute
    $targets = @((Get-Item -LiteralPath $BrowserDirectory -Force)) + @(Get-ChildItem -LiteralPath $BrowserDirectory -Recurse -Force)
    foreach ($target in $targets) {
        if (($target.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Chrome installation contains a reparse point; permission validation refused.' }
        $rules = (Get-Acl -LiteralPath $target.FullName).GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier])
        foreach ($capability in $capabilities) {
            $allowed = [Security.AccessControl.FileSystemRights]0
            foreach ($rule in $rules) {
                if (($rule.PropagationFlags -band [Security.AccessControl.PropagationFlags]::InheritOnly) -ne 0) { continue }
                $sid = $rule.IdentityReference.Value
                if ($rule.AccessControlType -eq [Security.AccessControl.AccessControlType]::Deny -and
                    $sid -in @($capability, 'S-1-1-0', 'S-1-15-2-1', 'S-1-15-2-2') -and ($rule.FileSystemRights -band $required) -ne 0) {
                    throw 'Chrome sandbox read/execute access is denied. Inspect the pinned installation ACL; no ACL reset or broader grant was attempted.'
                }
                if ($sid -eq $capability -and $rule.AccessControlType -eq [Security.AccessControl.AccessControlType]::Allow) {
                    $allowed = $allowed -bor $rule.FileSystemRights
                }
            }
            if (($allowed -band $required) -ne $required) {
                throw 'Chrome sandbox read/execute permissions are missing on an installation file/directory. Check protected inheritance and re-run setup; no broader grant was attempted.'
            }
        }
    }
}

function Invoke-ChromeSandboxSetup([string] $BrowserDirectory, [string] $Installer) {
    $start = [Diagnostics.ProcessStartInfo]::new($Installer)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.ArgumentList.Add('--configure-browser-in-directory=' + $BrowserDirectory)
    $process = [Diagnostics.Process]::Start($start)
    if ($null -eq $process) { throw 'Chrome sandbox permission setup could not start.' }
    try {
        # Discard installer output concurrently; setup stdout remains executable-path-only.
        $stdout = $process.StandardOutput.BaseStream.CopyToAsync([IO.Stream]::Null)
        $stderr = $process.StandardError.BaseStream.CopyToAsync([IO.Stream]::Null)
        if (-not $process.WaitForExit(30000)) {
            $process.Kill($true)
            throw 'Chrome sandbox permission setup exceeded 30000 ms; its owned process tree was terminated.'
        }
        [void][Threading.Tasks.Task]::WhenAll([Threading.Tasks.Task[]]@($stdout, $stderr)).WaitAsync([TimeSpan]::FromSeconds(5)).GetAwaiter().GetResult()
        Assert-ChromeSetupExitCode $process.ExitCode
    }
    finally {
        if (-not $process.HasExited) { $process.Kill($true) }
        $process.Dispose()
    }
    Assert-ChromeSandboxAccess $BrowserDirectory
}

$pin = Get-Content -LiteralPath $PinPath -Raw | ConvertFrom-Json
if ($pin.version -notmatch '^\d+\.\d+\.\d+\.\d+$' -or $pin.platform -notin @('win64', 'win32', 'linux64')) { throw 'Chrome pin has an invalid version/platform.' }
$cache = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($CacheDirectory)
$archive = Assert-ChromePath $cache (Join-Path $cache ('chrome-' + $pin.version + '-' + $pin.platform + '.zip'))
$install = Assert-ChromePath $cache (Join-Path $cache ('chrome-' + $pin.version + '-' + $pin.platform))
if ([IO.Path]::IsPathRooted($pin.executableRelativePath)) { throw 'Chrome executable pin must be a relative path.' }
$executable = Assert-ChromePath $install (Join-Path $install $pin.executableRelativePath)
New-Item -ItemType Directory -Force -Path $cache | Out-Null
if (-not (Test-Path -LiteralPath $archive)) {
    if ($Offline) { throw "Pinned Chrome archive is not available in the offline cache: $archive" }
    Invoke-WebRequest $pin.archiveUrl -OutFile $archive
}
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $pin.archiveSha256) { throw 'Chrome archive hash mismatch.' }
if (-not (Test-Path -LiteralPath $executable)) {
    if (Test-Path -LiteralPath $install) { throw 'Incomplete cached Chrome installation; choose a fresh cache directory rather than overwriting existing files.' }
    Expand-Archive -LiteralPath $archive -DestinationPath $install
}
[void](Assert-ChromePath $install $executable)
if ((Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash.ToLowerInvariant() -ne $pin.executableSha256) { throw 'Chrome executable hash mismatch. Use a fresh cache and investigate the changed installation.' }
if ($IsWindows) {
    $browserDirectory = [IO.Path]::GetDirectoryName($executable)
    $installer = Assert-ChromePath $install (Join-Path $browserDirectory 'setup.exe')
    if (@(Get-ChildItem -LiteralPath $install -Recurse -Force -Attributes ReparsePoint).Count -ne 0) { throw 'Chrome installation contains a reparse point; sandbox setup refused.' }
    # Authenticate setup.exe against the already hash-verified archive, including cache/offline reuse.
    $zip = [IO.Compression.ZipFile]::OpenRead($archive)
    try {
        $entryName = [IO.Path]::GetRelativePath($install, $installer).Replace('\', '/')
        $entry = $zip.GetEntry($entryName)
        if ($null -eq $entry) { throw 'Pinned Chrome archive does not contain its sandbox installer.' }
        $entryStream = $entry.Open()
        try { $installerHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($entryStream)) }
        finally { $entryStream.Dispose() }
    }
    finally { $zip.Dispose() }
    if ((Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash -ne $installerHash) { throw 'Chrome sandbox installer hash mismatch.' }
    Invoke-ChromeSandboxSetup $browserDirectory $installer
}
else {
    & chmod +x $executable
    if ($LASTEXITCODE -ne 0) { throw 'Chrome executable permissions could not be set.' }
}
Write-Output $executable
