[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "../.."))
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ("metricbudget-release-contract-tests-" + [Guid]::NewGuid().ToString("N"))
$validatorPath = Join-Path $testRoot "scripts/validate-release.ps1"
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
. (Join-Path $PSScriptRoot "pwsh-launch.ps1")
. (Join-Path $PSScriptRoot "../../build/Invoke-NestedPwsh.ps1")

function Assert-True {
    param([Parameter(Mandatory = $true)][bool] $Condition, [Parameter(Mandatory = $true)][string] $Message)

    if (-not $Condition) { throw "RELEASE_VALIDATION_TEST=FAIL: $Message" }
}

function Invoke-ReleaseValidator {
    param([Parameter(Mandatory = $true)][string[]] $ScriptArguments)

    $arguments = Get-PwshChildArguments -HideWindow $false -ScriptPath $validatorPath -ScriptArguments $ScriptArguments
    $output = @(Invoke-NestedPwsh -ArgumentList $arguments 2>&1)
    return [pscustomobject]@{
        ExitCode = $LASTEXITCODE
        Output = ($output -join "`n")
    }
}

function Write-Changelog {
    param(
        [Parameter(Mandatory = $true)][string] $Date,
        [string] $Version = "0.1.0",
        [string] $UnreleasedBody = ""
    )

    $content = [System.Collections.Generic.List[string]]::new()
    foreach ($line in [string[]]@(
        "# Changelog"
        ""
        "## [Unreleased]"
    )) { $content.Add($line) }
    if (-not [string]::IsNullOrWhiteSpace($UnreleasedBody))
    {
        $content.Add("")
        $content.Add("- $UnreleasedBody")
    }
    foreach ($line in [string[]]@(
        ""
        "## [$Version] - $Date"
        ""
        "### Added"
        ""
        "- Observed-cardinality verification."
    )) { $content.Add($line) }
    [IO.File]::WriteAllText((Join-Path $testRoot "CHANGELOG.md"), ($content -join "`n"), $utf8NoBom)
}

function Reset-Fixtures {
    foreach ($relativePath in @(
        "Directory.Build.props",
        "Directory.Packages.props",
        "README.md",
        "CHANGELOG.md",
        "src/KeelMatrix.MetricBudget/README.md",
        "src/KeelMatrix.MetricBudget/KeelMatrix.MetricBudget.csproj",
        "src/KeelMatrix.MetricBudget/PublicAPI.Shipped.txt",
        "src/KeelMatrix.MetricBudget/PublicAPI.Unshipped.txt",
        "scripts/validate-release.ps1"
    )) {
        $source = Join-Path $repositoryRoot $relativePath
        $destination = Join-Path $testRoot $relativePath
        New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($destination)) -Force | Out-Null
        Copy-Item -LiteralPath $source -Destination $destination -Force
    }
}

try {
    $windowsArguments = @(Get-PwshChildArguments -HideWindow $true -ScriptPath 'validator.ps1' -ScriptArguments @('-Version', '0.1.0'))
    Assert-True (($windowsArguments -join ' ') -eq '-NoProfile -WindowStyle Hidden -File validator.ps1 -Version 0.1.0') `
        "Windows child PowerShell arguments must hide the child window. Actual: $($windowsArguments -join ' ')"

    $portableArguments = @(Get-PwshChildArguments -HideWindow $false -ScriptPath 'validator.ps1' -ScriptArguments @('-Version', '0.1.0'))
    Assert-True (-not ($portableArguments -contains '-WindowStyle')) `
        "Non-Windows child PowerShell arguments must omit the Windows-only window-style switch. Actual: $($portableArguments -join ' ')"

    Reset-Fixtures

    Write-Changelog ([DateTime]::UtcNow.ToString("yyyy-MM-dd", [Globalization.CultureInfo]::InvariantCulture))
    $positive = Invoke-ReleaseValidator @('-Version', '0.1.0')
    Assert-True ($positive.ExitCode -eq 0 -and $positive.Output -match "RELEASE_VALIDATION=PASS") `
        "a finalized entry with today's real calendar date must pass. Output: $($positive.Output)"

    $tagPositive = Invoke-ReleaseValidator @('-Tag', 'v0.1.0')
    Assert-True ($tagPositive.ExitCode -eq 0 -and $tagPositive.Output -match "RELEASE_VALIDATION=PASS") `
        "a finalized entry must pass through the tag validation path. Output: $($tagPositive.Output)"

    Reset-Fixtures
    Write-Changelog ([DateTime]::UtcNow.ToString("yyyy-MM-dd", [Globalization.CultureInfo]::InvariantCulture)) `
        -UnreleasedBody "This must block a taggable first-release state."
    $unreleased = Invoke-ReleaseValidator @('-Version', '0.1.0')
    Assert-True ($unreleased.ExitCode -ne 0 -and $unreleased.Output -match "Unreleased") `
        "substantive Unreleased content must fail release validation. Output: $($unreleased.Output)"

    Reset-Fixtures
    Write-Changelog ([DateTime]::UtcNow.ToString("yyyy-MM-dd", [Globalization.CultureInfo]::InvariantCulture)) -Version "0.1.1"
    $missingTarget = Invoke-ReleaseValidator @('-Version', '0.1.0')
    Assert-True ($missingTarget.ExitCode -ne 0 -and $missingTarget.Output -match "CHANGELOG\.md") `
        "a missing target release entry must fail with a changelog diagnostic. Output: $($missingTarget.Output)"

    Write-Changelog "2026-2-3"
    $malformed = Invoke-ReleaseValidator @('-Version', '0.1.0')
    Assert-True ($malformed.ExitCode -ne 0 -and $malformed.Output -match "YYYY-MM-DD") `
        "a malformed release date must fail. Output: $($malformed.Output)"

    Write-Changelog "2026-02-30"
    $impossible = Invoke-ReleaseValidator @('-Version', '0.1.0')
    Assert-True ($impossible.ExitCode -ne 0) `
        "an impossible release date must fail. Output: $($impossible.Output)"

    Write-Changelog ([DateTime]::UtcNow.AddDays(1).ToString("yyyy-MM-dd", [Globalization.CultureInfo]::InvariantCulture))
    $future = Invoke-ReleaseValidator @('-Version', '0.1.0')
    Assert-True ($future.ExitCode -ne 0 -and $future.Output -match "future") `
        "a future release date must fail. Output: $($future.Output)"

    Reset-Fixtures
    Write-Changelog ([DateTime]::UtcNow.ToString("yyyy-MM-dd", [Globalization.CultureInfo]::InvariantCulture))
    $propsPath = Join-Path $testRoot "Directory.Build.props"
    $props = [IO.File]::ReadAllText($propsPath) -replace '<Version>0\.1\.0</Version>', '<Version>0.1.1</Version>'
    [IO.File]::WriteAllText($propsPath, $props, $utf8NoBom)
    $staleVersion = Invoke-ReleaseValidator @('-Version', '0.1.0')
    Assert-True ($staleVersion.ExitCode -ne 0 -and $staleVersion.Output -match "Directory.Build.props") `
        "a stale package version must fail. Output: $($staleVersion.Output)"

    Reset-Fixtures
    Write-Changelog ([DateTime]::UtcNow.ToString("yyyy-MM-dd", [Globalization.CultureInfo]::InvariantCulture))
    foreach ($readmePath in @("README.md", "src/KeelMatrix.MetricBudget/README.md")) {
        $path = Join-Path $testRoot $readmePath
        $readme = [IO.File]::ReadAllText($path) -replace '--version 0\.1\.0', '--version 0.0.9'
        [IO.File]::WriteAllText($path, $readme, $utf8NoBom)
    }
    $staleInstall = Invoke-ReleaseValidator @('-Version', '0.1.0')
    Assert-True ($staleInstall.ExitCode -ne 0 -and $staleInstall.Output -match "install") `
        "stale install metadata must fail. Output: $($staleInstall.Output)"

    Reset-Fixtures
    Write-Changelog ([DateTime]::UtcNow.ToString("yyyy-MM-dd", [Globalization.CultureInfo]::InvariantCulture))
    $unshippedPath = Join-Path $testRoot "src/KeelMatrix.MetricBudget/PublicAPI.Unshipped.txt"
    [IO.File]::WriteAllText($unshippedPath, "#nullable enable`nKeelMatrix.MetricBudget.StaleApi.get -> string!`n", $utf8NoBom)
    $apiDrift = Invoke-ReleaseValidator @('-Version', '0.1.0')
    Assert-True ($apiDrift.ExitCode -ne 0 -and $apiDrift.Output -match "PublicAPI.Unshipped.txt") `
        "a first-release shipped/unshipped API drift must fail. Output: $($apiDrift.Output)"

    $releaseWorkflow = [IO.File]::ReadAllText((Join-Path $repositoryRoot ".github/workflows/release.yml"))
    Assert-True ($releaseWorkflow.Contains('$validatorArguments = @(''-Tag'', $tag)')) `
        "tag release path must invoke the repository validator."
    Assert-True ($releaseWorkflow.Contains('$validatorArguments = @(''-Version'', $releaseVersion)')) `
        "manual release path must invoke the repository validator."
    Assert-True ($releaseWorkflow.Contains('& pwsh -NoProfile -File scripts/validate-release.ps1 @validatorArguments')) `
        "tag and manual release paths must share one repository validator invocation."
    Assert-True ($releaseWorkflow.Contains('pwsh -NoProfile -File scripts/verify-package.ps1 -RequireMainProvenance -ExpectedVersion $env:RELEASE_VERSION')) `
        "release packaging must use the guarded package gate for cleanup, packing, and inspection."
    Assert-True ($releaseWorkflow -notmatch '(?s)Clear release artifact directory.*?Get-ChildItem.*?Remove-Item') `
        "release packaging must not directly recursively delete the package output directory."
    Assert-True ($releaseWorkflow -notmatch '(?m)^\s*run:\s*dotnet pack\b') `
        "release packaging must not bypass the package gate with a raw dotnet pack command."

    Write-Output "RELEASE_VALIDATION_TEST=PASS cases=17"
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
