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
. (Join-Path $PSScriptRoot "../../build/Invoke-NestedPwsh.ps1")

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
        [Parameter(Mandatory = $true)][string] $Ref,
        [switch] $UseWorkflowCommandFiles
    )

    $previousGithubEnv = $env:GITHUB_ENV
    $previousGithubOutput = $env:GITHUB_OUTPUT
    $fixtureGithubEnv = Join-Path $testRoot ("fixture-github-env-" + [Guid]::NewGuid().ToString("N"))
    $fixtureGithubOutput = Join-Path $testRoot ("fixture-github-output-" + [Guid]::NewGuid().ToString("N"))
    if (-not $UseWorkflowCommandFiles) {
        $env:GITHUB_ENV = $fixtureGithubEnv
        $env:GITHUB_OUTPUT = $fixtureGithubOutput
    }

    Push-Location $candidate
    try {
        $arguments = Get-PwshChildArguments `
            -HideWindow $false `
            -ScriptPath (Join-Path $repositoryRoot "scripts/validate-release-provenance.ps1") `
            -ScriptArguments @('-EventName', $EventName, '-RefName', $RefName, '-Ref', $Ref)
        $output = @(Invoke-NestedPwsh -ArgumentList $arguments 2>&1)
        $currentGithubEnv = $env:GITHUB_ENV
        $currentGithubOutput = $env:GITHUB_OUTPUT
        $result = [pscustomobject]@{
            ExitCode = $LASTEXITCODE
            Output = ($output -join "`n")
            GithubEnv = if (-not [string]::IsNullOrWhiteSpace($currentGithubEnv) -and (Test-Path -LiteralPath $currentGithubEnv)) { Get-Content -Raw $currentGithubEnv } else { "" }
            GithubOutput = if (-not [string]::IsNullOrWhiteSpace($currentGithubOutput) -and (Test-Path -LiteralPath $currentGithubOutput)) { Get-Content -Raw $currentGithubOutput } else { "" }
        }
        return $result
    }
    finally {
        Pop-Location
        if ($null -eq $previousGithubEnv) {
            Remove-Item Env:GITHUB_ENV -ErrorAction SilentlyContinue
        }
        else {
            $env:GITHUB_ENV = $previousGithubEnv
        }

        if ($null -eq $previousGithubOutput) {
            Remove-Item Env:GITHUB_OUTPUT -ErrorAction SilentlyContinue
        }
        else {
            $env:GITHUB_OUTPUT = $previousGithubOutput
        }
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

    # Reproduce the workflow boundary: the real validator writes one immutable step output, then the fixture
    # runner executes validators in child processes with private command files. Fixture commits must not reach the
    # surrounding workflow command files that the later pack step consumes.
    $workflowGithubEnv = Join-Path $testRoot "workflow-github-env"
    $workflowGithubOutput = Join-Path $testRoot "workflow-github-output"
    $previousGithubEnv = $env:GITHUB_ENV
    $previousGithubOutput = $env:GITHUB_OUTPUT
    try {
        $env:GITHUB_ENV = $workflowGithubEnv
        $env:GITHUB_OUTPUT = $workflowGithubOutput
        $realValidation = Invoke-Validator 'push' 'v0.1.0' 'refs/tags/v0.1.0' -UseWorkflowCommandFiles
        Assert-True ($realValidation.ExitCode -eq 0) `
            "the real workflow provenance step must pass before fixture tests run. Output: $($realValidation.Output)"
        $realOutput = Get-Content -Raw $workflowGithubOutput
        Assert-True ($realOutput -match '^commit=[0-9a-f]{40}\s*$') `
            "the real validator must emit exactly one candidate commit step output. Actual: $realOutput"
        Assert-True (-not (Test-Path -LiteralPath $workflowGithubEnv)) `
            "the provenance validator must not mutate the job-wide environment command file."

        $fixtureValidation = Invoke-Validator 'push' 'v0.1.0' 'refs/tags/v0.1.0'
        Assert-True ($fixtureValidation.ExitCode -eq 0) `
            "fixture provenance validation must pass in its isolated command files. Output: $($fixtureValidation.Output)"
        Assert-True ($fixtureValidation.GithubOutput -match '^commit=[0-9a-f]{40}\s*$') `
            "fixture validation must write its result to its private output file. Actual: $($fixtureValidation.GithubOutput)"
        Assert-True ((Get-Content -Raw $workflowGithubOutput) -eq $realOutput) `
            "fixture validation must not overwrite the real workflow's verified commit output."
    }
    finally {
        if ($null -eq $previousGithubEnv) {
            Remove-Item Env:GITHUB_ENV -ErrorAction SilentlyContinue
        }
        else {
            $env:GITHUB_ENV = $previousGithubEnv
        }

        if ($null -eq $previousGithubOutput) {
            Remove-Item Env:GITHUB_OUTPUT -ErrorAction SilentlyContinue
        }
        else {
            $env:GITHUB_OUTPUT = $previousGithubOutput
        }
    }

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
    $malformedMessageIsSpecific = $malformed.Output -match 'stable' -and
        $malformed.Output -match 'release' -and
        $malformed.Output -match 'tag'
    Assert-True ($malformed.ExitCode -ne 0 -and $malformedMessageIsSpecific) `
        "a malformed tag ref must fail. Output: $($malformed.Output)"

    Write-Output "RELEASE_PROVENANCE_TEST=PASS cases=6"
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
