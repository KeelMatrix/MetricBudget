[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "../.."))
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ("metricbudget-release-provenance-tests-" + [Guid]::NewGuid().ToString("N"))
$origin = Join-Path $testRoot "origin.git"
$seed = Join-Path $testRoot "seed"
$candidate = Join-Path $testRoot "candidate"
. (Join-Path $PSScriptRoot "pwsh-launch.ps1")

function Assert-True {
    param([Parameter(Mandatory = $true)][bool] $Condition, [Parameter(Mandatory = $true)][string] $Message)

    if (-not $Condition) { throw "RELEASE_PROVENANCE_TEST=FAIL: $Message" }
}

function Invoke-Git {
    param(
        [Parameter(Mandatory = $true)][string] $WorkingDirectory,
        [Parameter(Mandatory = $true)][string[]] $Arguments
    )

    $output = @(& git -C $WorkingDirectory @Arguments 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "git -C '$WorkingDirectory' $($Arguments -join ' ') failed: $($output -join ' ')"
    }

    return ($output -join "`n").Trim()
}

function Invoke-Validator {
    param(
        [Parameter(Mandatory = $true)][string] $EventName,
        [Parameter(Mandatory = $true)][string] $RefName,
        [Parameter(Mandatory = $true)][string] $Ref
    )

    Push-Location $candidate
    try {
        $arguments = Get-PwshChildArguments `
            -HideWindow ([bool]$IsWindows) `
            -ScriptPath (Join-Path $repositoryRoot "scripts/validate-release-provenance.ps1") `
            -ScriptArguments @('-EventName', $EventName, '-RefName', $RefName, '-Ref', $Ref)
        $output = @(& pwsh @arguments 2>&1)
        return [pscustomobject]@{
            ExitCode = $LASTEXITCODE
            Output = ($output -join "`n")
        }
    }
    finally {
        Pop-Location
    }
}

function Write-SeedFile {
    param([Parameter(Mandatory = $true)][string] $Text)

    [IO.File]::WriteAllText((Join-Path $seed "state.txt"), $Text, (New-Object System.Text.UTF8Encoding($false)))
}

try {
    New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
    Invoke-Git $testRoot @('init', '--bare', '--initial-branch=main', $origin) | Out-Null
    New-Item -ItemType Directory -Path $seed -Force | Out-Null
    Invoke-Git $seed @('init', '--initial-branch=main') | Out-Null
    Invoke-Git $seed @('config', 'user.email', 'release-provenance-tests@example.invalid') | Out-Null
    Invoke-Git $seed @('config', 'user.name', 'Release Provenance Tests') | Out-Null
    Write-SeedFile "initial"
    Invoke-Git $seed @('add', 'state.txt') | Out-Null
    Invoke-Git $seed @('commit', '-m', 'initial') | Out-Null
    Invoke-Git $seed @('remote', 'add', 'origin', $origin) | Out-Null
    Invoke-Git $seed @('push', '-u', 'origin', 'main') | Out-Null

    Invoke-Git $testRoot @('clone', $origin, $candidate) | Out-Null
    Invoke-Git $candidate @('config', 'user.email', 'release-provenance-tests@example.invalid') | Out-Null
    Invoke-Git $candidate @('config', 'user.name', 'Release Provenance Tests') | Out-Null
    Invoke-Git $candidate @('tag', '-a', 'v0.1.0', '-m', 'release') | Out-Null

    $positive = Invoke-Validator 'push' 'v0.1.0' 'refs/tags/v0.1.0'
    Assert-True ($positive.ExitCode -eq 0 -and $positive.Output -match 'exact_main=PASS') `
        "current release tag on current main must pass. Output: $($positive.Output)"

    Write-SeedFile "main advanced"
    Invoke-Git $seed @('add', 'state.txt') | Out-Null
    Invoke-Git $seed @('commit', '-m', 'advance main') | Out-Null
    Invoke-Git $seed @('push', 'origin', 'main') | Out-Null

    $stale = Invoke-Validator 'push' 'v0.1.0' 'refs/tags/v0.1.0'
    Assert-True ($stale.ExitCode -ne 0 -and $stale.Output -match 'not exactly') `
        "a stale ancestor tag must fail after main advances. Output: $($stale.Output)"

    Invoke-Git $candidate @('checkout', '-b', 'feature') | Out-Null
    [IO.File]::WriteAllText((Join-Path $candidate "state.txt"), "feature", (New-Object System.Text.UTF8Encoding($false)))
    Invoke-Git $candidate @('add', 'state.txt') | Out-Null
    Invoke-Git $candidate @('commit', '-m', 'feature commit') | Out-Null
    Invoke-Git $candidate @('tag', '-a', 'v0.1.1', '-m', 'feature release') | Out-Null

    $nonMain = Invoke-Validator 'push' 'v0.1.1' 'refs/tags/v0.1.1'
    Assert-True ($nonMain.ExitCode -ne 0 -and $nonMain.Output -match 'not exactly') `
        "a tag on a non-main commit must fail. Output: $($nonMain.Output)"

    Invoke-Git $candidate @('checkout', 'origin/main') | Out-Null
    $manual = Invoke-Validator 'workflow_dispatch' 'main' 'refs/heads/main'
    Assert-True ($manual.ExitCode -eq 0 -and $manual.Output -match 'exact_main=PASS') `
        "manual validation on current main must pass. Output: $($manual.Output)"

    $wrongManualRef = Invoke-Validator 'workflow_dispatch' 'feature' 'refs/heads/feature'
    Assert-True ($wrongManualRef.ExitCode -ne 0 -and $wrongManualRef.Output -match 'refs/heads/main') `
        "manual validation from a non-main ref must fail. Output: $($wrongManualRef.Output)"

    $malformed = Invoke-Validator 'push' 'release-0.1.2' 'refs/tags/release-0.1.2'
    Assert-True ($malformed.ExitCode -ne 0 -and $malformed.Output -match 'stable release tag') `
        "a malformed tag ref must fail. Output: $($malformed.Output)"

    Write-Output "RELEASE_PROVENANCE_TEST=PASS cases=6"
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
