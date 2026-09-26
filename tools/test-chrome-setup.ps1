param([Parameter(Mandatory = $true)] [string] $ResultsDirectory)
$ErrorActionPreference = 'Stop'

# Load only the setup's function definitions; no provisioning/network activity in these unit scenarios.
$setupPath = Join-Path $PSScriptRoot 'setup-chrome.ps1'
$tokens = $null
$parseErrors = $null
$syntax = [Management.Automation.Language.Parser]::ParseFile($setupPath, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count -ne 0) { throw 'Chrome setup script has syntax errors.' }
foreach ($definition in $syntax.FindAll({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] }, $false)) {
    . ([ScriptBlock]::Create($definition.Extent.Text))
}
function Assert-Rejected([scriptblock] $Action, [string] $Expected) {
    try { & $Action; throw 'Unexpected acceptance' }
    catch { if ($_.Exception.Message -notlike $Expected) { throw "Wrong failure: $_ (expected $Expected)" } }
}
$run = Join-Path ([IO.Path]::GetFullPath($ResultsDirectory)) ([Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $run -Force | Out-Null
$cache = Join-Path $run 'cache'
New-Item -ItemType Directory -Path $cache | Out-Null
Assert-Rejected { Assert-ChromePath $cache $cache } '*strictly inside*'
Assert-Rejected { Assert-ChromePath $cache (Join-Path $run 'cache-sibling/browser') } '*strictly inside*'
Assert-Rejected { Assert-ChromePath $cache (Join-Path $cache '../outside') } '*strictly inside*'
[void](Assert-ChromePath $cache (Join-Path $cache 'install/browser'))
Assert-ChromeSetupExitCode 0
Assert-ChromeSetupExitCode 78
Assert-Rejected { Assert-ChromeSetupExitCode 79 } '*permission setup failed*'
Assert-Rejected { Assert-ChromeSetupExitCode -1 } '*permission setup failed*'
if ($IsWindows) {
    $outside = Join-Path $run 'outside'
    New-Item -ItemType Directory -Path $outside | Out-Null
    $junction = Join-Path $cache 'junction'
    New-Item -ItemType Junction -Path $junction -Target $outside | Out-Null
    Assert-Rejected { Assert-ChromePath $cache (Join-Path $junction 'browser') } '*reparse point*'
    $browser = Join-Path $cache 'browser'
    New-Item -ItemType Directory -Path $browser | Out-Null
    [IO.File]::WriteAllText((Join-Path $browser 'chrome.exe'), 'test resource, not executable')
    $parentBefore = (Get-Acl -LiteralPath $cache).Sddl
    Assert-Rejected { Assert-ChromeSandboxAccess $browser } '*permissions are missing*'
    $acl = Get-Acl -LiteralPath $browser
    foreach ($sid in @(
        'S-1-15-3-1024-3424233489-972189580-2057154623-747635277-1604371224-316187997-3786583170-1043257646',
        'S-1-15-3-1024-2302894289-466761758-1166120688-1039016420-2430351297-4240214049-4028510897-3317428798')) {
        $identity = [Security.Principal.SecurityIdentifier]::new($sid)
        $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($identity, 'ReadAndExecute', 'ContainerInherit,ObjectInherit', 'None', 'Allow'))
    }
    Set-Acl -LiteralPath $browser -AclObject $acl
    Assert-ChromeSandboxAccess $browser
    $sddl = (Get-Acl -LiteralPath $browser).Sddl
    Assert-ChromeSandboxAccess $browser
    if ((Get-Acl -LiteralPath $browser).Sddl -cne $sddl) { throw 'Read-only validation changed an ACL.' }
    if ((Get-Acl -LiteralPath $cache).Sddl -cne $parentBefore) { throw 'Parent ACL changed.' }
    $deny = Get-Acl -LiteralPath (Join-Path $browser 'chrome.exe')
    $deny.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($identity, 'ReadAndExecute', 'Deny'))
    Set-Acl -LiteralPath (Join-Path $browser 'chrome.exe') -AclObject $deny
    Assert-Rejected { Assert-ChromeSandboxAccess $browser } '*access is denied*'
}
Write-Output "Chrome setup boundary/permission scenarios passed: $run"
