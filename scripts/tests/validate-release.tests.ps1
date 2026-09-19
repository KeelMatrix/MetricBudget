[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "../.."))
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ("metricbudget-release-date-tests-" + [Guid]::NewGuid().ToString("N"))
$validatorPath = Join-Path $testRoot "scripts/validate-release.ps1"
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

function Assert-True {
    param([Parameter(Mandatory = $true)][bool] $Condition, [Parameter(Mandatory = $true)][string] $Message)

    if (-not $Condition) { throw "RELEASE_VALIDATION_TEST=FAIL: $Message" }
}

function Invoke-ReleaseValidator {
    $output = @(& pwsh -NoProfile -File $validatorPath -Version 0.1.0 2>&1)
    return [pscustomobject]@{
        ExitCode = $LASTEXITCODE
        Output = ($output -join "`n")
    }
}

function Write-Changelog {
    param([Parameter(Mandatory = $true)][string] $Date)

    $content = @(
        "# Changelog"
        ""
        "## [Unreleased]"
        ""
        "## [0.1.0] - $Date"
        ""
        "### Added"
        ""
        "- Observed-cardinality verification."
    ) -join "`n"
    [IO.File]::WriteAllText((Join-Path $testRoot "CHANGELOG.md"), $content, $utf8NoBom)
}

try {
    foreach ($relativePath in @(
        "Directory.Build.props",
        "Directory.Packages.props",
        "README.md",
        "CHANGELOG.md",
        "src/KeelMatrix.MetricBudget/README.md",
        "src/KeelMatrix.MetricBudget/KeelMatrix.MetricBudget.csproj",
        "scripts/validate-release.ps1"
    )) {
        $source = Join-Path $repositoryRoot $relativePath
        $destination = Join-Path $testRoot $relativePath
        New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($destination)) -Force | Out-Null
        Copy-Item -LiteralPath $source -Destination $destination
    }

    Write-Changelog ([DateTime]::UtcNow.ToString("yyyy-MM-dd", [Globalization.CultureInfo]::InvariantCulture))
    $positive = Invoke-ReleaseValidator
    Assert-True ($positive.ExitCode -eq 0 -and $positive.Output -match "RELEASE_VALIDATION=PASS") `
        "a finalized entry with today's real calendar date must pass. Output: $($positive.Output)"

    Write-Changelog "2026-2-3"
    $malformed = Invoke-ReleaseValidator
    Assert-True ($malformed.ExitCode -ne 0 -and $malformed.Output -match "YYYY-MM-DD") `
        "a malformed release date must fail. Output: $($malformed.Output)"

    Write-Changelog "2026-02-30"
    $impossible = Invoke-ReleaseValidator
    Assert-True ($impossible.ExitCode -ne 0) `
        "an impossible release date must fail. Output: $($impossible.Output)"

    Write-Changelog ([DateTime]::UtcNow.AddDays(1).ToString("yyyy-MM-dd", [Globalization.CultureInfo]::InvariantCulture))
    $future = Invoke-ReleaseValidator
    Assert-True ($future.ExitCode -ne 0 -and $future.Output -match "future") `
        "a future release date must fail. Output: $($future.Output)"

    Write-Output "RELEASE_VALIDATION_TEST=PASS cases=4"
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
