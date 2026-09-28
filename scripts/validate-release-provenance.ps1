[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $EventName,
    [Parameter(Mandatory = $true)][string] $RefName,
    [Parameter(Mandatory = $true)][string] $Ref
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Fail {
    param([Parameter(Mandatory = $true)][string] $Message)

    throw "RELEASE_SOURCE=FAIL: $Message"
}

function Invoke-GitText {
    param([Parameter(Mandatory = $true)][string[]] $Arguments)

    $output = @(& git @Arguments 2>&1)
    if ($LASTEXITCODE -ne 0) {
        Fail "git $($Arguments -join ' ') failed: $($output -join ' ')"
    }

    return ($output -join "`n").Trim()
}

$fetchOutput = @(& git fetch --no-tags --prune origin '+refs/heads/main:refs/remotes/origin/main' 2>&1)
if ($LASTEXITCODE -ne 0) {
    Fail "could not fetch origin/main: $($fetchOutput -join ' ')"
}

$head = Invoke-GitText @('rev-parse', '--verify', 'HEAD^{commit}')
$main = Invoke-GitText @('rev-parse', '--verify', 'refs/remotes/origin/main^{commit}')
if ([string]::IsNullOrWhiteSpace($head) -or [string]::IsNullOrWhiteSpace($main)) {
    Fail "could not resolve both HEAD and the fetched origin/main commit."
}

if ($EventName -eq 'push') {
    if ($RefName -notmatch '^v\d+\.\d+\.\d+$') {
        Fail "push ref '$RefName' is not a stable release tag v<major>.<minor>.<patch>."
    }

    if ($Ref -ne "refs/tags/$RefName") {
        Fail "push ref '$Ref' does not exactly identify tag '$RefName'."
    }

    $tagCommit = Invoke-GitText @('rev-parse', '--verify', "$RefName^{commit}")
    if ($tagCommit -ne $head) {
        Fail "tag '$RefName' resolves to '$tagCommit', but checked-out HEAD is '$head'."
    }
}
elseif ($EventName -eq 'workflow_dispatch') {
    if ($RefName -ne 'main' -or $Ref -ne 'refs/heads/main') {
        Fail "manual validation must run from refs/heads/main, not '$Ref'."
    }
}
else {
    Fail "unsupported release event '$EventName'."
}

if ($head -ne $main) {
    Fail "checked-out HEAD '$head' is not exactly the fetched origin/main commit '$main'."
}

Write-Output "RELEASE_SOURCE ref=$Ref sha=$head exact_main=PASS"
if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_ENV)) {
    "RELEASE_COMMIT=$head" | Out-File -FilePath $env:GITHUB_ENV -Encoding utf8 -Append
}
