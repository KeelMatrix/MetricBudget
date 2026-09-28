[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "../.."))
. (Join-Path $repositoryRoot "scripts/verify-package.ps1") -FunctionsOnly

function Assert-True {
    param([Parameter(Mandatory = $true)][bool] $Condition, [Parameter(Mandatory = $true)][string] $Message)

    if (-not $Condition) { throw "VERIFY_PACKAGE_TEST=FAIL: $Message" }
}

function Assert-Throws {
    param(
        [Parameter(Mandatory = $true)][scriptblock] $Action,
        [Parameter(Mandatory = $true)][string] $Message
    )

    try
    {
        & $Action
    }
    catch
    {
        return
    }

    throw "VERIFY_PACKAGE_TEST=FAIL: expected failure: $Message"
}

function New-AuditAttempt {
    param(
        [Parameter(Mandatory = $true)][int] $ExitCode,
        [AllowEmptyString()][string] $StandardOutput = "",
        [AllowEmptyString()][string] $StandardError = ""
    )

    return [pscustomobject]@{
        ExitCode = $ExitCode
        StandardOutput = $StandardOutput
        StandardError = $StandardError
        CombinedOutput = $StandardOutput + $StandardError
    }
}

function Invoke-TestGit {
    param(
        [Parameter(Mandatory = $true)][string] $Root,
        [Parameter(Mandatory = $true)][string[]] $Arguments
    )

    $output = @(& git -C $Root @Arguments 2>&1)
    if ($LASTEXITCODE -ne 0)
    {
        throw "Test git command failed: git -C $Root $($Arguments -join ' ')`n$($output -join "`n")"
    }

    return ($output -join "`n").Trim()
}

$documentedMarkdownFiles = @(Get-ChildItem -LiteralPath $repositoryRoot -Recurse -File -Filter "*.md" |
    Where-Object {
        $_.FullName -notmatch "[\\/](?:\.git|artifacts|bin|obj)[\\/]" -and
        $_.FullName -notmatch "[\\/]ApprovedShippedText[\\/]" -and
        $_.Name -ne "CHANGELOG.md"
    })
$rawPackSurfaces = @($documentedMarkdownFiles | ForEach-Object {
    $text = [IO.File]::ReadAllText($_.FullName)
    if ($text -match "(?i)\bdotnet\s+pack\b")
    {
        $_.FullName.Substring($repositoryRoot.Length + 1)
    }
})
Assert-True ($rawPackSurfaces.Count -eq 0) `
    "release-facing Markdown must route package validation through scripts/verify-package.ps1; raw dotnet pack found in: $($rawPackSurfaces -join ', ')"

$provenanceTestRoot = Join-Path ([IO.Path]::GetTempPath()) ("metricbudget-package-provenance-tests-" + [Guid]::NewGuid().ToString("N"))
try
{
    New-Item -ItemType Directory -Path $provenanceTestRoot -Force | Out-Null
    Invoke-TestGit $provenanceTestRoot @("init", "--initial-branch=main") | Out-Null
    Invoke-TestGit $provenanceTestRoot @("config", "user.email", "package-provenance-tests@example.invalid") | Out-Null
    Invoke-TestGit $provenanceTestRoot @("config", "user.name", "Package Provenance Tests") | Out-Null
    [IO.File]::WriteAllText((Join-Path $provenanceTestRoot "state.txt"), "candidate")
    Invoke-TestGit $provenanceTestRoot @("add", "state.txt") | Out-Null
    Invoke-TestGit $provenanceTestRoot @("commit", "-m", "candidate") | Out-Null
    $origin = Join-Path $provenanceTestRoot "origin.git"
    Invoke-TestGit $provenanceTestRoot @("init", "--bare", $origin) | Out-Null
    Invoke-TestGit $provenanceTestRoot @("remote", "add", "origin", $origin) | Out-Null
    Invoke-TestGit $provenanceTestRoot @("push", "-u", "origin", "main") | Out-Null

    $mainCommit = Get-RepositoryCommit -RepositoryRoot $provenanceTestRoot
    $mainProperties = @(Get-PackageRepositoryProperties -ExpectedCommit $mainCommit -RepositoryRoot $provenanceTestRoot)
    Assert-True ($mainProperties -contains "-p:RepositoryBranch=refs/heads/main") `
        "package provenance must set the release branch explicitly"
    Assert-True ($mainProperties -contains "-p:RepositoryCommit=$mainCommit") `
        "package provenance must set the exact verified checked-out commit"
    Assert-Throws { Get-PackageRepositoryProperties -ExpectedCommit "" -RepositoryRoot $provenanceTestRoot } `
        "package provenance must reject an absent commit"

    Invoke-TestGit $provenanceTestRoot @("checkout", "--detach", "--quiet", "HEAD") | Out-Null
    $detachedCommit = Get-RepositoryCommit -RepositoryRoot $provenanceTestRoot
    $detachedProperties = @(Get-PackageRepositoryProperties -ExpectedCommit $detachedCommit -RepositoryRoot $provenanceTestRoot)
    Assert-True ($detachedCommit -eq $mainCommit) "detached checkouts must retain the exact candidate commit"
    Assert-True (($detachedProperties -join "`n") -eq ($mainProperties -join "`n")) `
        "main and detached checkouts must receive identical package provenance properties"

    $advancer = Join-Path $provenanceTestRoot "advancer"
    Invoke-TestGit $provenanceTestRoot @("clone", $origin, $advancer) | Out-Null
    Invoke-TestGit $advancer @("config", "user.email", "package-provenance-tests@example.invalid") | Out-Null
    Invoke-TestGit $advancer @("config", "user.name", "Package Provenance Tests") | Out-Null
    [IO.File]::WriteAllText((Join-Path $advancer "state.txt"), "advanced")
    Invoke-TestGit $advancer @("add", "state.txt") | Out-Null
    Invoke-TestGit $advancer @("commit", "-m", "advance main") | Out-Null
    Invoke-TestGit $advancer @("push", "origin", "main") | Out-Null

    Assert-Throws { Get-PackageRepositoryProperties -ExpectedCommit $detachedCommit -RepositoryRoot $provenanceTestRoot } `
        "a stale ancestor must not synthesize main provenance"

    Invoke-TestGit $provenanceTestRoot @("checkout", "-b", "feature") | Out-Null
    [IO.File]::WriteAllText((Join-Path $provenanceTestRoot "state.txt"), "feature")
    Invoke-TestGit $provenanceTestRoot @("add", "state.txt") | Out-Null
    Invoke-TestGit $provenanceTestRoot @("commit", "-m", "feature") | Out-Null
    $featureCommit = Get-RepositoryCommit -RepositoryRoot $provenanceTestRoot
    Assert-Throws { Get-PackageRepositoryProperties -ExpectedCommit $featureCommit -RepositoryRoot $provenanceTestRoot } `
        "a non-main feature commit must not synthesize main provenance"

    Invoke-TestGit $provenanceTestRoot @("checkout", "--detach", "origin/main") | Out-Null
    $currentMainCommit = Get-RepositoryCommit -RepositoryRoot $provenanceTestRoot
    $currentMainProperties = @(Get-PackageRepositoryProperties -ExpectedCommit $currentMainCommit -RepositoryRoot $provenanceTestRoot)
    Assert-True (($currentMainProperties -join "`n") -match "-p:RepositoryCommit=$currentMainCommit") `
        "a detached checkout of current origin/main must retain exact provenance"

    $withoutOrigin = Join-Path $provenanceTestRoot "without-origin"
    New-Item -ItemType Directory -Path $withoutOrigin -Force | Out-Null
    Invoke-TestGit $withoutOrigin @("init", "--initial-branch=main") | Out-Null
    Invoke-TestGit $withoutOrigin @("config", "user.email", "package-provenance-tests@example.invalid") | Out-Null
    Invoke-TestGit $withoutOrigin @("config", "user.name", "Package Provenance Tests") | Out-Null
    [IO.File]::WriteAllText((Join-Path $withoutOrigin "state.txt"), "no origin")
    Invoke-TestGit $withoutOrigin @("add", "state.txt") | Out-Null
    Invoke-TestGit $withoutOrigin @("commit", "-m", "no origin") | Out-Null
    $withoutOriginCommit = Get-RepositoryCommit -RepositoryRoot $withoutOrigin
    Assert-Throws { Get-PackageRepositoryProperties -ExpectedCommit $withoutOriginCommit -RepositoryRoot $withoutOrigin } `
        "a repository without an origin must fail closed"

    Invoke-TestGit $provenanceTestRoot @("remote", "set-url", "origin", (Join-Path $provenanceTestRoot "missing.git")) | Out-Null
    Assert-Throws { Get-PackageRepositoryProperties -ExpectedCommit $currentMainCommit -RepositoryRoot $provenanceTestRoot } `
        "a broken origin must fail closed"
}
finally
{
    if (Test-Path -LiteralPath $provenanceTestRoot)
    {
        Remove-Item -LiteralPath $provenanceTestRoot -Recurse -Force
    }
}

$expectedProjects = @("src/lib/MyProject.csproj")
$cleanReport = @'
{
  "version": 1,
  "parameters": "--vulnerable --include-transitive",
  "sources": ["https://api.nuget.org/v3/index.json"],
  "projects": [
    {
      "path": "src/lib/MyProject.csproj",
      "frameworks": [
        {
          "framework": "net8.0",
          "topLevelPackages": [],
          "transitivePackages": []
        }
      ]
    }
  ]
}
'@
$cleanPathOnlyReport = @'
{
  "version": 1,
  "parameters": "--vulnerable --include-transitive",
  "sources": ["https://api.nuget.org/v3/index.json"],
  "projects": [
    {
      "path": "src/lib/MyProject.csproj"
    }
  ]
}
'@
$findingReport = @'
{
  "version": 1,
  "parameters": "--vulnerable --include-transitive",
  "sources": ["https://api.nuget.org/v3/index.json"],
  "projects": [
    {
      "path": "src/lib/MyProject.csproj",
      "frameworks": [
        {
          "framework": "net8.0",
          "topLevelPackages": [
            {
              "id": "Example.Vulnerable",
              "requestedVersion": "1.0.0",
              "resolvedVersion": "1.0.0",
              "vulnerabilities": [
                {
                  "severity": "High",
                  "advisoryurl": "https://example.test/advisory"
                }
              ]
            }
          ],
          "transitivePackages": []
        }
      ]
    }
  ]
}
'@

$cleanAssessment = Get-VulnerabilityReportAssessment $cleanReport $expectedProjects
Assert-True $cleanAssessment.IsValid "valid clean machine-readable output must be understood"
Assert-True (-not $cleanAssessment.HasVulnerability) "clean output must prove no finding"
Assert-True (-not (Test-VulnerabilityResult $cleanReport $expectedProjects)) "clean output must not report a finding"
Assert-True ((Get-VulnerabilityReportAssessment $cleanPathOnlyReport $expectedProjects).IsValid) `
    "the current clean CLI shape with project paths and omitted empty frameworks must remain supported"
Assert-True (Test-VulnerabilityResult $findingReport $expectedProjects) "a finding must be detected from JSON structure"
Assert-Throws { Test-VulnerabilityResult ($cleanReport -replace '"framework": "net8.0"', '"framework": 8') $expectedProjects } `
    "numeric framework names must fail closed"
Assert-Throws { Test-VulnerabilityResult ($cleanReport -replace '"topLevelPackages": \[\]', '"topLevelPackages": [{"id": 42, "resolvedVersion": "1.0.0"}]') $expectedProjects } `
    "non-text package identifiers must fail closed"
Assert-Throws { Test-VulnerabilityResult ($findingReport -replace '"severity": "High"', '"severity": 4') $expectedProjects } `
    "non-text advisory fields must fail closed"

Assert-Throws { Test-VulnerabilityResult "Paquetes vulnerables: ninguno" $expectedProjects } `
    "localized console text must not be accepted as evidence"
Assert-Throws { Test-VulnerabilityResult "" $expectedProjects } "empty output must fail closed"
Assert-Throws { Test-VulnerabilityResult '{"version":1' $expectedProjects } "malformed output must fail closed"
Assert-Throws { Test-VulnerabilityResult ($cleanReport -replace '"version": 1', '"version": 2') $expectedProjects } `
    "unsupported schema output must fail closed"
Assert-Throws { Test-VulnerabilityResult ($cleanReport -replace '--include-transitive', '--direct-only') $expectedProjects } `
    "missing transitive coverage must fail closed"
Assert-Throws { Test-VulnerabilityResult ($cleanReport -replace 'src/lib/MyProject.csproj', 'src/lib/Other.csproj') $expectedProjects } `
    "missing expected project coverage must fail closed"
Assert-Throws { Test-VulnerabilityResult ($cleanReport -replace '"sources": \["https://api.nuget.org/v3/index.json"\]', '"sources": []') $expectedProjects } `
    "missing advisory sources must fail closed"
Assert-Throws { Test-VulnerabilityResult ($cleanReport -replace '"sources": \["https://api.nuget.org/v3/index.json"\]', '"sources": "https://api.nuget.org/v3/index.json"') $expectedProjects } `
    "scalar advisory sources must fail closed"
Assert-Throws { Test-VulnerabilityResult '{"version":1,"parameters":"--vulnerable --include-transitive","sources":["https://api.nuget.org/v3/index.json"],"projects":{"path":"src/lib/MyProject.csproj"}}' $expectedProjects } `
    "scalar project coverage must fail closed"
Assert-Throws { Test-VulnerabilityResult ($cleanReport -replace '"projects": \[', '"problems": [{"level":"error","text":"advisory service failed"}], "projects": [') $expectedProjects } `
    "reported audit problems must fail closed"

$attemptCount = 0
$machineReadableArguments = $false
$retryResult = Invoke-VulnerabilityAudit -ExpectedProjectPaths $expectedProjects -AttemptInvoker {
    param($attempt, $arguments)
    $script:attemptCount++
    if (($arguments -join " ") -match '--format json' -and ($arguments -join " ") -match '--output-version 1')
    {
        $script:machineReadableArguments = $true
    }
    if ($attempt -eq 1)
    {
        return New-AuditAttempt 1 "" "error NU1900: unable to load the advisory service"
    }

    return New-AuditAttempt 0 $cleanReport ""
}
Assert-True ($attemptCount -eq 2) "a genuine advisory-service failure gets exactly one bounded retry"
Assert-True $machineReadableArguments "the audit must request versioned machine-readable JSON output"

$findingAttemptCount = 0
Assert-Throws {
    Invoke-VulnerabilityAudit -ExpectedProjectPaths $expectedProjects -AttemptInvoker {
        param($attempt, $arguments)
        $script:findingAttemptCount++
        return New-AuditAttempt 0 $findingReport ""
    }
} "a finding must not be retried or converted to success"
Assert-True ($findingAttemptCount -eq 1) "findings must stop the audit without retry"

$unrecognizedAttemptCount = 0
Assert-Throws {
    Invoke-VulnerabilityAudit -ExpectedProjectPaths $expectedProjects -AttemptInvoker {
        param($attempt, $arguments)
        $script:unrecognizedAttemptCount++
        return New-AuditAttempt 0 "Vulnerabilidades: ninguna" ""
    }
} "zero-exit unrecognized output must not pass"
Assert-True ($unrecognizedAttemptCount -eq 1) "unrecognized evidence must not be retried as a service failure"

$malformedNetworkTextAttemptCount = 0
Assert-Throws {
    Invoke-VulnerabilityAudit -ExpectedProjectPaths $expectedProjects -AttemptInvoker {
        param($attempt, $arguments)
        $script:malformedNetworkTextAttemptCount++
        return New-AuditAttempt 0 '{"version":1,"note":"network"}' ""
    }
} "malformed report text must not be treated as a transient service failure"
Assert-True ($malformedNetworkTextAttemptCount -eq 1) "missing evidence must not be retried from stdout text"

$persistentFailureCount = 0
Assert-Throws {
    Invoke-VulnerabilityAudit -ExpectedProjectPaths $expectedProjects -AttemptInvoker {
        param($attempt, $arguments)
        $script:persistentFailureCount++
        return New-AuditAttempt 1 "" "error NU1301: unable to load the service index"
    }
} "a second advisory-service failure must fail the gate"
Assert-True ($persistentFailureCount -eq 2) "advisory-service failure retry must remain bounded"

Write-Output "VERIFY_PACKAGE_TEST=PASS cases=25"
