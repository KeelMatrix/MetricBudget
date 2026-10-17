$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$validatorPath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "../validate-website-metadata.ps1"))
$scratchRoot = if (-not [string]::IsNullOrWhiteSpace($env:PAPERCLIP_RUN_SCRATCH_DIR)) {
    $env:PAPERCLIP_RUN_SCRATCH_DIR
} elseif (-not [string]::IsNullOrWhiteSpace($env:PAPERCLIP_SCRATCH_DIR)) {
    $env:PAPERCLIP_SCRATCH_DIR
} else {
    [IO.Path]::GetTempPath()
}
$scratchRoot = [IO.Path]::GetFullPath($scratchRoot)
New-Item -ItemType Directory -Path $scratchRoot -Force | Out-Null
$fixtureRoot = Join-Path $scratchRoot ("MetricBudget-website-metadata-" + [Guid]::NewGuid().ToString("N"))
$firstProjectDirectory = Join-Path $fixtureRoot "src/KeelMatrix.MetricBudget"
$secondProjectDirectory = Join-Path $fixtureRoot "src/KeelMatrix.MetricBudget.Component"
$firstProjectPath = Join-Path $firstProjectDirectory "KeelMatrix.MetricBudget.csproj"
$secondProjectPath = Join-Path $secondProjectDirectory "KeelMatrix.MetricBudget.Component.csproj"

function Write-Fixture {
    param(
        [string] $Tags = "metrics;keelmatrix-public-product;keelmatrix-primary",
        [string] $Visibility = "public-product",
        [string] $Role = "primary",
        [string] $ProjectUrl = "https://github.com/KeelMatrix/MetricBudget",
        [string] $RepositoryUrl = "https://github.com/KeelMatrix/MetricBudget",
        [string] $Description = "Verify observed metric cardinality in .NET tests and CI.",
        [string] $PackageReadmeFile = "README.md",
        [string] $PackageType = "",
        [string] $PackAsTool = "",
        [int] $SchemaVersion = 1,
        [switch] $SecondPackage,
        [string] $SecondRole = "component",
        [switch] $OmitSecondManifestEntry,
        [switch] $MissingReadmeFile,
        [switch] $ExtraManifestEntry,
        [switch] $MissingManifestEntry
    )

    New-Item -ItemType Directory -Path $firstProjectDirectory -Force | Out-Null
    $packageTypeElement = if ([string]::IsNullOrWhiteSpace($PackageType)) { "" } else { "<PackageType>$PackageType</PackageType>" }
    $packAsToolElement = if ([string]::IsNullOrWhiteSpace($PackAsTool)) { "" } else { "<PackAsTool>$PackAsTool</PackAsTool>" }
    $firstProject = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <IsPackable>true</IsPackable>
    <PackageId>KeelMatrix.MetricBudget</PackageId>
    <Description>$Description</Description>
    <PackageTags>$Tags</PackageTags>
    <PackageProjectUrl>$ProjectUrl</PackageProjectUrl>
    <RepositoryUrl>$RepositoryUrl</RepositoryUrl>
    <RepositoryType>git</RepositoryType>
    <PackageReadmeFile>$PackageReadmeFile</PackageReadmeFile>
    <Authors>KeelMatrix</Authors>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    $packageTypeElement
    $packAsToolElement
  </PropertyGroup>
</Project>
"@
    Set-Content -LiteralPath $firstProjectPath -Value $firstProject -Encoding utf8NoBOM
    $firstReadmePath = Join-Path $firstProjectDirectory "README.md"
    if ($MissingReadmeFile)
    {
        if (Test-Path -LiteralPath $firstReadmePath)
        {
            Remove-Item -LiteralPath $firstReadmePath -Force
        }
    }
    else
    {
        Set-Content -LiteralPath $firstReadmePath -Value "# Fixture package" -Encoding utf8NoBOM
    }

    if ($SecondPackage)
    {
        New-Item -ItemType Directory -Path $secondProjectDirectory -Force | Out-Null
        $secondTag = if ($SecondRole -eq "primary") { "keelmatrix-primary" } else { "keelmatrix-component" }
        $secondProject = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <IsPackable>true</IsPackable>
    <PackageId>KeelMatrix.MetricBudget.Component</PackageId>
    <Description>Component package for the MetricBudget fixture.</Description>
    <PackageTags>metrics;keelmatrix-public-product;$secondTag</PackageTags>
    <PackageProjectUrl>https://github.com/KeelMatrix/MetricBudget</PackageProjectUrl>
    <RepositoryUrl>https://github.com/KeelMatrix/MetricBudget</RepositoryUrl>
    <RepositoryType>git</RepositoryType>
    <PackageReadmeFile>README.md</PackageReadmeFile>
    <Authors>KeelMatrix</Authors>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
  </PropertyGroup>
</Project>
"@
        Set-Content -LiteralPath $secondProjectPath -Value $secondProject -Encoding utf8NoBOM
        Set-Content -LiteralPath (Join-Path $secondProjectDirectory "README.md") -Value "# Fixture component" -Encoding utf8NoBOM
    }
    elseif (Test-Path -LiteralPath $secondProjectPath)
    {
        Remove-Item -LiteralPath $secondProjectPath -Force
    }

    $packages = [ordered]@{}
    if (-not $MissingManifestEntry)
    {
        $packages["KeelMatrix.MetricBudget"] = [ordered]@{
            visibility = $Visibility
            role = $Role
        }
    }
    if ($SecondPackage)
    {
        if (-not $OmitSecondManifestEntry)
        {
            $packages["KeelMatrix.MetricBudget.Component"] = [ordered]@{
                visibility = "public-product"
                role = $SecondRole
            }
        }
    }
    if ($ExtraManifestEntry)
    {
        $packages["KeelMatrix.MetricBudget.Missing"] = [ordered]@{
            visibility = "public-product"
            role = "component"
        }
    }
    $manifest = [ordered]@{
        schemaVersion = $SchemaVersion
        packages = $packages
    }
    Set-Content -LiteralPath (Join-Path $fixtureRoot "keelmatrix.website.json") -Value ($manifest | ConvertTo-Json -Depth 5) -Encoding utf8NoBOM
}

function Invoke-ValidatorCase {
    param(
        [Parameter(Mandatory = $true)][string] $Name,
        [Parameter(Mandatory = $true)][bool] $ShouldPass
    )

    $pwshPath = (Get-Command pwsh).Source
    $output = & $pwshPath -NoLogo -NoProfile -File $validatorPath -RepositoryRoot $fixtureRoot 2>&1
    $exitCode = $LASTEXITCODE
    $text = ($output | ForEach-Object { [string]$_ }) -join [Environment]::NewLine
    if ($ShouldPass)
    {
        if ($exitCode -ne 0 -or $text -notmatch "WEBSITE_METADATA=PASS")
        {
            throw "Expected website metadata validator case '$Name' to pass, but it failed: $text"
        }
    }
    elseif ($exitCode -eq 0 -or $text -notmatch "WEBSITE_METADATA=FAIL")
    {
        throw "Expected website metadata validator case '$Name' to fail, but it passed."
    }
    Write-Output "WEBSITE_METADATA_TEST=PASS case=$Name"
}

try
{
    New-Item -ItemType Directory -Path $fixtureRoot -Force | Out-Null

    Write-Fixture
    Invoke-ValidatorCase -Name "valid-single-primary" -ShouldPass $true

    Write-Fixture -MissingManifestEntry
    Invoke-ValidatorCase -Name "missing-manifest-entry" -ShouldPass $false

    Write-Fixture -ExtraManifestEntry
    Invoke-ValidatorCase -Name "stale-manifest-entry" -ShouldPass $false

    Write-Fixture -SchemaVersion 2
    Invoke-ValidatorCase -Name "unsupported-schema" -ShouldPass $false

    Write-Fixture
    Set-Content -LiteralPath (Join-Path $fixtureRoot "keelmatrix.website.json") -Value "{malformed" -Encoding utf8NoBOM
    Invoke-ValidatorCase -Name "malformed-manifest" -ShouldPass $false

    Write-Fixture
    Set-Content -LiteralPath (Join-Path $fixtureRoot "keelmatrix.website.json") -Value '{"schemaVersion":1,"schemaVersion":1,"packages":{"KeelMatrix.MetricBudget":{"visibility":"public-product","role":"primary"}}}' -Encoding utf8NoBOM
    Invoke-ValidatorCase -Name "duplicate-schema-property" -ShouldPass $false

    Write-Fixture
    Set-Content -LiteralPath (Join-Path $fixtureRoot "keelmatrix.website.json") -Value '{"schemaVersion":1,"packages":{"KeelMatrix.MetricBudget":{"visibility":"public-product","role":"primary"},"KeelMatrix.MetricBudget":{"visibility":"public-product","role":"primary"}}}' -Encoding utf8NoBOM
    Invoke-ValidatorCase -Name "duplicate-package-entry" -ShouldPass $false

    Write-Fixture
    $unknownPropertyManifest = '{"schemaVersion":1,"packages":{"KeelMatrix.MetricBudget":{"visibility":"public-product","role":"primary","description":"not classification"}}}'
    Set-Content -LiteralPath (Join-Path $fixtureRoot "keelmatrix.website.json") -Value $unknownPropertyManifest -Encoding utf8NoBOM
    Invoke-ValidatorCase -Name "manifest-copy-rejects-description" -ShouldPass $false

    Write-Fixture -Visibility "Public-product"
    Invoke-ValidatorCase -Name "wrong-manifest-classification-case" -ShouldPass $false

    Write-Fixture -Tags "metrics;keelmatrix-primary"
    Invoke-ValidatorCase -Name "missing-visibility-sentinel" -ShouldPass $false

    Write-Fixture -Tags "metrics;keelmatrix-public-product;keelmatrix-internal-package;keelmatrix-primary"
    Invoke-ValidatorCase -Name "duplicate-visibility-sentinels" -ShouldPass $false

    Write-Fixture -Tags "metrics;KeelMatrix-public-product;keelmatrix-primary"
    Invoke-ValidatorCase -Name "wrong-sentinel-case" -ShouldPass $false

    Write-Fixture -Tags "metrics;keelmatrix-public-product;keelmatrix-primary" -Visibility "internal-package"
    Invoke-ValidatorCase -Name "visibility-manifest-disagreement" -ShouldPass $false

    Write-Fixture -Tags "metrics;keelmatrix-public-product"
    Invoke-ValidatorCase -Name "missing-role-sentinel" -ShouldPass $false

    Write-Fixture -Tags "metrics;keelmatrix-public-product;keelmatrix-primary;keelmatrix-component"
    Invoke-ValidatorCase -Name "duplicate-role-sentinels" -ShouldPass $false

    Write-Fixture -Tags "metrics;keelmatrix-public-product;keelmatrix-component" -Role "primary"
    Invoke-ValidatorCase -Name "role-manifest-disagreement" -ShouldPass $false

    Write-Fixture -Tags "metrics;keelmatrix-public-product;keelmatrix-component" -Role "component"
    Invoke-ValidatorCase -Name "missing-primary" -ShouldPass $false

    Write-Fixture -SecondPackage -SecondRole "primary"
    Invoke-ValidatorCase -Name "duplicate-primary" -ShouldPass $false

    Write-Fixture -SecondPackage -OmitSecondManifestEntry
    Invoke-ValidatorCase -Name "unlisted-packable-project" -ShouldPass $false

    Write-Fixture -SecondPackage
    Invoke-ValidatorCase -Name "primary-and-component-family" -ShouldPass $true

    Write-Fixture -ProjectUrl "https://github.com/KeelMatrix/MetricBudget#readme"
    Invoke-ValidatorCase -Name "noncanonical-project-url" -ShouldPass $false

    Write-Fixture -RepositoryUrl "https://github.com/KeelMatrix/Other"
    Invoke-ValidatorCase -Name "repository-url-mismatch" -ShouldPass $false

    Write-Fixture -Description ""
    Invoke-ValidatorCase -Name "missing-description" -ShouldPass $false

    Write-Fixture -PackageType "DotnetTool"
    Invoke-ValidatorCase -Name "dependency-package-type" -ShouldPass $false

    Write-Fixture -PackAsTool "true"
    Invoke-ValidatorCase -Name "dependency-pack-as-tool" -ShouldPass $false

    Write-Fixture -PackageReadmeFile "missing.md"
    Invoke-ValidatorCase -Name "missing-package-readme" -ShouldPass $false

    Write-Fixture -MissingReadmeFile
    Invoke-ValidatorCase -Name "missing-readme-file" -ShouldPass $false
}
finally
{
    $scratchPrefix = $scratchRoot.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) +
        [IO.Path]::DirectorySeparatorChar
    $resolvedFixtureRoot = [IO.Path]::GetFullPath($fixtureRoot)
    if (-not $resolvedFixtureRoot.StartsWith($scratchPrefix, [StringComparison]::OrdinalIgnoreCase))
    {
        throw "Refusing to clean a validator fixture outside the scratch root: $resolvedFixtureRoot"
    }
    if (Test-Path -LiteralPath $resolvedFixtureRoot)
    {
        Remove-Item -LiteralPath $resolvedFixtureRoot -Recurse -Force
    }
}

Write-Output "WEBSITE_METADATA_TESTS=PASS"
