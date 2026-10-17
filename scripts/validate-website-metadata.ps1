[CmdletBinding()]
param(
    [string] $RepositoryRoot = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if ([string]::IsNullOrWhiteSpace($RepositoryRoot))
{
    $RepositoryRoot = Join-Path $PSScriptRoot ".."
}
$RepositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot)
$canonicalRepositoryUrl = "https://github.com/KeelMatrix/MetricBudget"
$manifestPath = Join-Path $RepositoryRoot "keelmatrix.website.json"

function Fail {
    param([Parameter(Mandatory = $true)][string] $Message)

    throw "WEBSITE_METADATA=FAIL: $Message"
}

function Get-JsonString {
    param(
        [Parameter(Mandatory = $true)][System.Text.Json.JsonElement] $Element,
        [Parameter(Mandatory = $true)][string] $PropertyName,
        [Parameter(Mandatory = $true)][string] $Context
    )

    $value = [System.Text.Json.JsonElement]::new()
    if (-not $Element.TryGetProperty($PropertyName, [ref]$value) -or
        $value.ValueKind -ne [System.Text.Json.JsonValueKind]::String)
    {
        Fail "$Context must define string '$PropertyName'."
    }

    return $value.GetString()
}

if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf))
{
    Fail "Root keelmatrix.website.json is missing."
}

try
{
    $manifestDocument = [System.Text.Json.JsonDocument]::Parse([IO.File]::ReadAllText($manifestPath))
}
catch
{
    Fail "Root keelmatrix.website.json is not valid strict JSON: $($_.Exception.Message)"
}

$manifestRoot = $manifestDocument.RootElement
if ($manifestRoot.ValueKind -ne [System.Text.Json.JsonValueKind]::Object)
{
    $manifestDocument.Dispose()
    Fail "The manifest root must be a JSON object."
}

$rootPropertyNames = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($property in $manifestRoot.EnumerateObject())
{
    if (-not $rootPropertyNames.Add($property.Name))
    {
        $manifestDocument.Dispose()
        Fail "The manifest repeats root property '$($property.Name)'."
    }
    if ($property.Name -notin @("schemaVersion", "packages"))
    {
        $manifestDocument.Dispose()
        Fail "The manifest contains unsupported root property '$($property.Name)'."
    }
}

$schemaVersion = [System.Text.Json.JsonElement]::new()
if (-not $manifestRoot.TryGetProperty("schemaVersion", [ref]$schemaVersion) -or
    $schemaVersion.ValueKind -ne [System.Text.Json.JsonValueKind]::Number -or
    -not [StringComparer]::Ordinal.Equals($schemaVersion.GetRawText(), "1"))
{
    $manifestDocument.Dispose()
    Fail "The manifest must use schemaVersion 1."
}

$packagesElement = [System.Text.Json.JsonElement]::new()
if (-not $manifestRoot.TryGetProperty("packages", [ref]$packagesElement) -or
    $packagesElement.ValueKind -ne [System.Text.Json.JsonValueKind]::Object)
{
    $manifestDocument.Dispose()
    Fail "The manifest 'packages' value must be an object."
}

$manifestPackages = [System.Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
foreach ($package in $packagesElement.EnumerateObject())
{
    if ([string]::IsNullOrWhiteSpace($package.Name))
    {
        $manifestDocument.Dispose()
        Fail "Manifest package IDs must not be empty."
    }
    if ($package.Value.ValueKind -ne [System.Text.Json.JsonValueKind]::Object)
    {
        $manifestDocument.Dispose()
        Fail "Manifest entry '$($package.Name)' must be an object."
    }

    $entryNames = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($entryProperty in $package.Value.EnumerateObject())
    {
        if (-not $entryNames.Add($entryProperty.Name))
        {
            $manifestDocument.Dispose()
            Fail "Manifest entry '$($package.Name)' repeats property '$($entryProperty.Name)'."
        }
        if ($entryProperty.Name -notin @("visibility", "role"))
        {
            $manifestDocument.Dispose()
            Fail "Manifest entry '$($package.Name)' contains unsupported property '$($entryProperty.Name)'."
        }
    }

    $context = "Manifest entry '$($package.Name)'"
    $visibility = Get-JsonString -Element $package.Value -PropertyName "visibility" -Context $context
    $role = Get-JsonString -Element $package.Value -PropertyName "role" -Context $context
    if (-not [StringComparer]::Ordinal.Equals($visibility, "public-product") -and
        -not [StringComparer]::Ordinal.Equals($visibility, "internal-package"))
    {
        $manifestDocument.Dispose()
        Fail "$context has unsupported visibility '$visibility'."
    }
    if (-not [StringComparer]::Ordinal.Equals($role, "primary") -and
        -not [StringComparer]::Ordinal.Equals($role, "component"))
    {
        $manifestDocument.Dispose()
        Fail "$context has unsupported role '$role'."
    }

    if (-not $manifestPackages.TryAdd($package.Name, [pscustomobject]@{
        Visibility = $visibility
        Role = $role
    }))
    {
        $manifestDocument.Dispose()
        Fail "The manifest repeats package ID '$($package.Name)'."
    }
}
$manifestDocument.Dispose()

if ($manifestPackages.Count -eq 0)
{
    Fail "The manifest must name at least one package."
}

$propertyNames = "IsPackable,PackageId,Description,PackageTags,PackageProjectUrl,RepositoryUrl,RepositoryType,PackageReadmeFile,Authors,PackageLicenseExpression,PackageType,PackAsTool"
$projectFiles = @(
    Get-ChildItem -LiteralPath $RepositoryRoot -Filter "*.csproj" -File -Recurse |
        Where-Object { $_.FullName -notmatch "[\/](bin|obj)[\/]" }
)
if ($projectFiles.Count -eq 0)
{
    Fail "No project files were found under the repository root."
}

$packableProjects = [System.Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
foreach ($projectFile in $projectFiles)
{
    $msbuildOutput = & dotnet msbuild $projectFile.FullName -nologo "-getProperty:$propertyNames"
    if ($LASTEXITCODE -ne 0)
    {
        Fail "MSBuild could not evaluate project '$($projectFile.FullName)'."
    }

    try
    {
        $evaluation = ($msbuildOutput -join [Environment]::NewLine) | ConvertFrom-Json -AsHashtable -ErrorAction Stop
        $properties = $evaluation.Properties
    }
    catch
    {
        Fail "MSBuild returned invalid property output for '$($projectFile.FullName)': $($_.Exception.Message)"
    }

    $isPackable = [string]$properties["IsPackable"]
    if (-not [StringComparer]::OrdinalIgnoreCase.Equals($isPackable, "true") -and
        -not [StringComparer]::OrdinalIgnoreCase.Equals($isPackable, "false"))
    {
        Fail "Project '$($projectFile.FullName)' did not evaluate IsPackable to true or false."
    }
    if ([StringComparer]::OrdinalIgnoreCase.Equals($isPackable, "false"))
    {
        continue
    }

    $packageId = [string]$properties["PackageId"]
    if ([string]::IsNullOrWhiteSpace($packageId))
    {
        Fail "Packable project '$($projectFile.FullName)' has no PackageId."
    }
    if ($packableProjects.ContainsKey($packageId))
    {
        Fail "More than one packable project produces package ID '$packageId'."
    }
    $packableProjects.Add($packageId, [pscustomobject]@{
        ProjectFile = $projectFile.FullName
        ProjectDirectory = $projectFile.DirectoryName
        Properties = $properties
    })
}

if ($packableProjects.Count -eq 0)
{
    Fail "No packable projects were found."
}

foreach ($packageId in $manifestPackages.Keys)
{
    if (-not $packableProjects.ContainsKey($packageId))
    {
        Fail "Manifest package '$packageId' has no packable project in this repository."
    }
}
foreach ($packageId in $packableProjects.Keys)
{
    if (-not $manifestPackages.ContainsKey($packageId))
    {
        Fail "Packable package '$packageId' is missing from keelmatrix.website.json."
    }
}

$familyRoles = [System.Collections.Generic.Dictionary[string, int]]::new([StringComparer]::Ordinal)
foreach ($packageId in $packableProjects.Keys)
{
    $project = $packableProjects[$packageId]
    $properties = $project.Properties
    $classification = $manifestPackages[$packageId]

    if (-not [StringComparer]::Ordinal.Equals([string]$properties["PackageId"], $packageId))
    {
        Fail "Evaluated PackageId does not match manifest package '$packageId'."
    }
    # Description accuracy is reviewed against consumer documentation; this check guards presence and package consistency.
    if ([string]::IsNullOrWhiteSpace([string]$properties["Description"]))
    {
        Fail "Packable package '$packageId' has no description."
    }
    if (-not [StringComparer]::Ordinal.Equals([string]$properties["Authors"], "KeelMatrix"))
    {
        Fail "Packable package '$packageId' must identify KeelMatrix as its author."
    }
    if (-not [StringComparer]::Ordinal.Equals([string]$properties["PackageLicenseExpression"], "MIT"))
    {
        Fail "Packable package '$packageId' must declare the repository's MIT license expression."
    }
    if (-not [StringComparer]::Ordinal.Equals([string]$properties["PackageProjectUrl"], $canonicalRepositoryUrl))
    {
        Fail "PackageProjectUrl for '$packageId' must be exactly '$canonicalRepositoryUrl'."
    }
    if (-not [StringComparer]::Ordinal.Equals([string]$properties["RepositoryUrl"], $canonicalRepositoryUrl))
    {
        Fail "RepositoryUrl for '$packageId' must be exactly '$canonicalRepositoryUrl'."
    }
    if (-not [StringComparer]::Ordinal.Equals([string]$properties["RepositoryType"], "git"))
    {
        Fail "RepositoryType for '$packageId' must be 'git'."
    }

    $readmeFile = [string]$properties["PackageReadmeFile"]
    if (-not [StringComparer]::Ordinal.Equals($readmeFile, "README.md"))
    {
        Fail "PackageReadmeFile for '$packageId' must be README.md."
    }
    $readmePath = Join-Path $project.ProjectDirectory $readmeFile
    if (-not (Test-Path -LiteralPath $readmePath -PathType Leaf))
    {
        Fail "Package README for '$packageId' is missing at '$readmePath'."
    }

    $packageType = [string]$properties["PackageType"]
    if (-not [string]::IsNullOrWhiteSpace($packageType) -and
        -not [StringComparer]::Ordinal.Equals($packageType, "Dependency"))
    {
        Fail "Package '$packageId' is inventoried as a Dependency and cannot declare PackageType '$packageType'."
    }
    $packAsTool = [string]$properties["PackAsTool"]
    if ([StringComparer]::OrdinalIgnoreCase.Equals($packAsTool, "true"))
    {
        Fail "Package '$packageId' is inventoried as a Dependency and cannot set PackAsTool=true."
    }
    if (-not [string]::IsNullOrWhiteSpace($packAsTool) -and
        -not [StringComparer]::OrdinalIgnoreCase.Equals($packAsTool, "false"))
    {
        Fail "PackAsTool for '$packageId' must be false or unset."
    }

    $tags = @(([string]$properties["PackageTags"] -split ";") | ForEach-Object { $_.Trim() })
    $visibilityTags = @($tags | Where-Object {
        [StringComparer]::OrdinalIgnoreCase.Equals($_, "keelmatrix-public-product") -or
        [StringComparer]::OrdinalIgnoreCase.Equals($_, "keelmatrix-internal-package")
    })
    $roleTags = @($tags | Where-Object {
        [StringComparer]::OrdinalIgnoreCase.Equals($_, "keelmatrix-primary") -or
        [StringComparer]::OrdinalIgnoreCase.Equals($_, "keelmatrix-component")
    })
    if ($visibilityTags.Count -ne 1)
    {
        Fail "Package '$packageId' must contain exactly one website visibility sentinel tag."
    }
    if ($roleTags.Count -ne 1)
    {
        Fail "Package '$packageId' must contain exactly one website role sentinel tag."
    }

    $expectedVisibilityTag = if ($classification.Visibility -eq "public-product") {
        "keelmatrix-public-product"
    } else {
        "keelmatrix-internal-package"
    }
    $expectedRoleTag = if ($classification.Role -eq "primary") {
        "keelmatrix-primary"
    } else {
        "keelmatrix-component"
    }
    if (-not [StringComparer]::Ordinal.Equals([string]$visibilityTags[0], $expectedVisibilityTag))
    {
        Fail "Package '$packageId' visibility sentinel does not agree with the manifest."
    }
    if (-not [StringComparer]::Ordinal.Equals([string]$roleTags[0], $expectedRoleTag))
    {
        Fail "Package '$packageId' role sentinel does not agree with the manifest."
    }

    $familyUrl = [string]$properties["PackageProjectUrl"]
    if (-not $familyRoles.ContainsKey($familyUrl))
    {
        $familyRoles.Add($familyUrl, 0)
    }
    if ($classification.Role -eq "primary")
    {
        $familyRoles[$familyUrl]++
    }
}

foreach ($familyUrl in $familyRoles.Keys)
{
    if ($familyRoles[$familyUrl] -ne 1)
    {
        Fail "Product family '$familyUrl' must have exactly one primary package; found $($familyRoles[$familyUrl])."
    }
}

Write-Output "WEBSITE_METADATA=PASS packages=$($packableProjects.Count) primary=$(@($manifestPackages.Keys | Where-Object { $manifestPackages[$_].Role -eq 'primary' }) -join ',')"
