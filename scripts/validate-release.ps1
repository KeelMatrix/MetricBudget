[CmdletBinding()]
param(
    [string] $Tag = "",
    [string] $Version = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))

function Fail {
    param([Parameter(Mandatory = $true)][string] $Message)

    throw "RELEASE_VALIDATION=FAIL: $Message"
}

function Get-ProjectXml {
    param([Parameter(Mandatory = $true)][string] $RelativePath)

    $path = Join-Path $repositoryRoot $RelativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf))
    {
        Fail "Required repository file '$RelativePath' is missing."
    }

    return [Xml](Get-Content -Raw $path)
}

function Get-PropertyValue {
    param(
        [Parameter(Mandatory = $true)] $Document,
        [Parameter(Mandatory = $true)][string] $Name,
        [Parameter(Mandatory = $true)][string] $FileName
    )

    $node = $Document.SelectSingleNode("/*[local-name()='Project']/*[local-name()='PropertyGroup']/*[local-name()='$Name']")
    if ($null -eq $node -or [string]::IsNullOrWhiteSpace($node.InnerText))
    {
        Fail "'$FileName' must define <$Name>."
    }

    return $node.InnerText.Trim()
}

if ([string]::IsNullOrWhiteSpace($Tag) -and [string]::IsNullOrWhiteSpace($Version))
{
    Fail "Provide -Tag vX.Y.Z or -Version X.Y.Z."
}

$tagVersion = $null
if (-not [string]::IsNullOrWhiteSpace($Tag))
{
    if ($Tag -notmatch '^v(?<version>\d+\.\d+\.\d+)$')
    {
        Fail "Tag format mismatch: '$Tag' must match v<major>.<minor>.<patch>, for example v0.1.0."
    }

    $tagVersion = $Matches.version
}

if (-not [string]::IsNullOrWhiteSpace($Version) -and $Version -notmatch '^\d+\.\d+\.\d+$')
{
    Fail "Version format mismatch: '$Version' must match <major>.<minor>.<patch>."
}

if ([string]::IsNullOrWhiteSpace($Version))
{
    $Version = $tagVersion
}
elseif ($null -ne $tagVersion -and $Version -ne $tagVersion)
{
    Fail "Tag/version mismatch: tag '$Tag' resolves to '$tagVersion', but -Version is '$Version'."
}

$buildProps = Get-ProjectXml "Directory.Build.props"
$configuredVersion = Get-PropertyValue $buildProps "Version" "Directory.Build.props"
if ($configuredVersion -ne $Version)
{
    Fail "Tag/version mismatch: release version '$Version' does not equal Directory.Build.props <Version> '$configuredVersion'."
}

$project = Get-ProjectXml "src/KeelMatrix.MetricBudget/KeelMatrix.MetricBudget.csproj"
$packageId = Get-PropertyValue $project "PackageId" "src/KeelMatrix.MetricBudget/KeelMatrix.MetricBudget.csproj"
if ($packageId -ne "KeelMatrix.MetricBudget")
{
    Fail "Package metadata mismatch: PackageId is '$packageId', expected 'KeelMatrix.MetricBudget'."
}

$packageVersion = Get-PropertyValue $project "PackageVersion" "src/KeelMatrix.MetricBudget/KeelMatrix.MetricBudget.csproj"
if ($packageVersion -ne "`$(Version)")
{
    Fail "Package metadata mismatch: PackageVersion is '$packageVersion', expected '`$(Version)'."
}

$targetFrameworks = Get-PropertyValue $project "TargetFrameworks" "src/KeelMatrix.MetricBudget/KeelMatrix.MetricBudget.csproj"
$actualTargetFrameworks = @($targetFrameworks.Split(';') | ForEach-Object { $_.Trim() } | Where-Object { $_ }) | Sort-Object
$expectedTargetFrameworks = @("net8.0", "netstandard2.0") | Sort-Object
if ((@($actualTargetFrameworks) -join ";") -ne (@($expectedTargetFrameworks) -join ";"))
{
    Fail "Package metadata mismatch: target frameworks are '$targetFrameworks', expected 'net8.0;netstandard2.0'."
}

$packages = Get-ProjectXml "Directory.Packages.props"
$packageVersions = @{}
foreach ($node in @($packages.SelectNodes("/*[local-name()='Project']/*[local-name()='ItemGroup']/*[local-name()='PackageVersion']")))
{
    $packageVersions[$node.GetAttribute("Include")] = $node.GetAttribute("Version")
}

if (-not $packageVersions.ContainsKey("KeelMatrix.Telemetry") -or $packageVersions["KeelMatrix.Telemetry"] -ne "[0.1.0]")
{
    Fail "Dependency metadata mismatch: KeelMatrix.Telemetry must use the exact range [0.1.0]."
}
if (-not $packageVersions.ContainsKey("System.Diagnostics.DiagnosticSource") -or $packageVersions["System.Diagnostics.DiagnosticSource"] -ne "8.0.1")
{
    Fail "Dependency metadata mismatch: System.Diagnostics.DiagnosticSource must use version 8.0.1."
}
if (-not $packageVersions.ContainsKey("KeelMatrix.MetricBudget") -or $packageVersions["KeelMatrix.MetricBudget"] -ne "[$Version]")
{
    Fail "Dependency metadata mismatch: package-consumer KeelMatrix.MetricBudget range must be [$Version]."
}

$changelogPath = Join-Path $repositoryRoot "CHANGELOG.md"
$changelogLines = @(Get-Content $changelogPath)
$releaseHeadingPattern = '^##\s+\[(?<version>\d+\.\d+\.\d+)\](?:\s*-\s*(?<date>.+))?\s*$'
$releaseStart = -1
$releaseDate = $null
for ($index = 0; $index -lt $changelogLines.Count; $index++)
{
    if ($changelogLines[$index] -match $releaseHeadingPattern -and $Matches.version -eq $Version)
    {
        $releaseStart = $index
        $releaseDate = $Matches.date
        break
    }
}

if ($releaseStart -lt 0)
{
    Fail "Changelog mismatch: CHANGELOG.md has no released entry for version '$Version'; an Unreleased-only entry cannot pass."
}
if ([string]::IsNullOrWhiteSpace($releaseDate))
{
    Fail "Changelog mismatch: released version '$Version' must include a release date in its heading."
}

$releaseEnd = $changelogLines.Count
for ($index = $releaseStart + 1; $index -lt $changelogLines.Count; $index++)
{
    if ($changelogLines[$index] -match '^##\s+')
    {
        $releaseEnd = $index
        break
    }
}
$releaseLines = @($changelogLines[$releaseStart..($releaseEnd - 1)])
$releaseText = $releaseLines -join "`n"

if ($releaseText -match '(?i)\bplanned\b|\bunreleased\b|not\s+yet\s+published|not\s+published|\bpending\b|\bTBD\b')
{
    Fail "Changelog mismatch: released version '$Version' is still described as planned, unreleased, pending, TBD, or not yet published."
}

$categories = @($releaseLines | ForEach-Object {
        if ($_ -match '^###\s+(?<category>.+?)\s*$') { $Matches.category.Trim() }
    })
if ($categories.Count -eq 0 -or $categories -notcontains "Added")
{
    Fail "Changelog structure mismatch: released version '$Version' must contain an Added section."
}
$unsupportedCategory = @($categories | Where-Object { $_ -ne "Added" } | Select-Object -First 1)
if ($unsupportedCategory.Count -gt 0)
{
    Fail "Changelog structure mismatch: first release '$Version' must contain only an Added section; found '$unsupportedCategory'."
}
if ($releaseText -match '(?i)\bnow\b|\bno longer\b|\bpreviously\b|\bformerly\b|\bused to\b|\bfixed\b|\bfixes\b|\bcorrected\b|\bresolved\b|\baddressed\b|this removes|this fixes|changed from')
{
    Fail "Changelog wording mismatch: first-release entry '$Version' contains pre-release remediation or transition wording."
}

function Get-LogicalReadmeLines {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyString()][string[]] $Lines,
        [Parameter(Mandatory = $true)][string] $ReadmeRelativePath
    )

    $logicalLines = @()
    $index = 0
    while ($index -lt $Lines.Count)
    {
        $startLine = $index + 1
        $text = $Lines[$index]

        while ($true)
        {
            $hasContinuation = $text -match '`\s*$' -or $text -match '\\\s*$'
            $hasVersionBreak = $text -match '(?i)--version\s*$'
            if (-not $hasContinuation -and -not $hasVersionBreak)
            {
                break
            }

            if ($index + 1 -ge $Lines.Count)
            {
                Fail "Could not reconstruct '$ReadmeRelativePath' line ${startLine}: continuation has no following line."
            }

            if ($hasContinuation)
            {
                $text = $text -replace '`\s*$', ''
                $text = $text -replace '\\\s*$', ''
            }

            $index++
            $text = $text.TrimEnd() + ' ' + $Lines[$index].Trim()
        }

        $logicalLines += [pscustomobject]@{
            Text = $text
            LineNumber = $startLine
        }
        $index++
    }

    return @($logicalLines)
}

function Get-LineNumberAtOffset {
    param(
        [Parameter(Mandatory = $true)][string] $Text,
        [Parameter(Mandatory = $true)][int] $Offset
    )

    return ([regex]::Matches($Text.Substring(0, $Offset), "\r?\n").Count + 1)
}

function Assert-InstallVersion {
    param(
        [Parameter(Mandatory = $true)][string] $ReadmeRelativePath,
        [Parameter(Mandatory = $true)][int] $LineNumber,
        [Parameter(Mandatory = $true)][string] $Kind,
        [Parameter(Mandatory = $true)][AllowEmptyString()][string] $Arguments
    )

    $versionMatch = [regex]::Match($Arguments, '(?i)(?:^|\s)(?:--version|-Version|-v)(?:\s+(?<version>[^\s`]+))?')
    if (-not $versionMatch.Success -or -not $versionMatch.Groups["version"].Success)
    {
        Fail "Install command mismatch: '$ReadmeRelativePath' line $LineNumber ($Kind) must name release version '$Version'."
    }

    $installVersion = $versionMatch.Groups["version"].Value
    if ($installVersion -ne $Version)
    {
        Fail "Install command/version mismatch: '$ReadmeRelativePath' line $LineNumber ($Kind) names '$installVersion', expected '$Version'."
    }
}

function Assert-VersionTokens {
    param(
        [Parameter(Mandatory = $true)][string] $ReadmeRelativePath,
        [Parameter(Mandatory = $true)][int] $LineNumber,
        [Parameter(Mandatory = $true)][AllowEmptyString()][string] $Text
    )

    foreach ($versionToken in [regex]::Matches($Text, '(?i)(?<![\w-])--version(?:\s+(?<version>[^\s`]+))?'))
    {
        if (-not $versionToken.Groups["version"].Success)
        {
            Fail "Version token mismatch: '$ReadmeRelativePath' line $LineNumber (--version) must name release version '$Version'."
        }

        $tokenVersion = $versionToken.Groups["version"].Value
        if ($tokenVersion -ne $Version)
        {
            Fail "Version token mismatch: '$ReadmeRelativePath' line $LineNumber (--version) names '$tokenVersion', expected '$Version'."
        }
    }
}

foreach ($readmeRelativePath in @("README.md", "src/KeelMatrix.MetricBudget/README.md"))
{
    $readmePath = Join-Path $repositoryRoot $readmeRelativePath
    if (-not (Test-Path -LiteralPath $readmePath -PathType Leaf))
    {
        Fail "Required README '$readmeRelativePath' is missing."
    }

    try
    {
        $readmeLines = [IO.File]::ReadAllLines($readmePath)
        $readmeText = [IO.File]::ReadAllText($readmePath)
    }
    catch
    {
        Fail "Could not read README '$readmeRelativePath': $($_.Exception.Message)"
    }

    $logicalLines = Get-LogicalReadmeLines -Lines $readmeLines -ReadmeRelativePath $readmeRelativePath
    $installOccurrenceCount = 0
    $installCommandPattern = '(?i)\b(?<kind>dotnet\s+add\s+package|Install-Package|nuget\s+install)\s+KeelMatrix\.MetricBudget\b(?<arguments>.*?)(?=\s+(?:dotnet\s+add\s+package|Install-Package|nuget\s+install)\s+KeelMatrix\.MetricBudget\b|$)'
    foreach ($logicalLine in $logicalLines)
    {
        $line = $logicalLine.Text
        $lineNumber = $logicalLine.LineNumber
        $commandMatches = [regex]::Matches($line, $installCommandPattern)
        foreach ($match in $commandMatches)
        {
            $installOccurrenceCount++
            Assert-InstallVersion $readmeRelativePath $lineNumber $match.Groups["kind"].Value $match.Groups["arguments"].Value
        }

        if ($line -match '(?i)\b(?:dotnet\s+add\s+package|Install-Package|nuget\s+install)\s*$')
        {
            Fail "Install command mismatch: '$readmeRelativePath' line $lineNumber contains a package command that cannot be attributed to a package."
        }
    }

    # Independent total rule: every --version token is checked separately
    # from command recognition, so an unrecognized command cannot bypass it.
    foreach ($logicalLine in $logicalLines)
    {
        Assert-VersionTokens $readmeRelativePath $logicalLine.LineNumber $logicalLine.Text
    }

    $packageReferenceStarts = [regex]::Matches($readmeText, '(?i)<PackageReference\b')
    $packageReferences = [regex]::Matches($readmeText, '(?is)<PackageReference\b(?<attributes>[^>]*?)>')
    if ($packageReferenceStarts.Count -ne $packageReferences.Count)
    {
        $lineNumber = if ($packageReferenceStarts.Count -gt $packageReferences.Count) {
            Get-LineNumberAtOffset $readmeText $packageReferenceStarts[$packageReferences.Count].Index
        }
        else { 1 }
        Fail "Could not reconstruct '$readmeRelativePath' line ${lineNumber}: an incomplete PackageReference element was found."
    }

    foreach ($packageReference in $packageReferences)
    {
        $attributes = $packageReference.Groups["attributes"].Value
        $includeMatch = [regex]::Match($attributes, '(?i)\bInclude\s*=\s*["'']KeelMatrix\.MetricBudget["'']')
        if (-not $includeMatch.Success)
        {
            continue
        }

        $installOccurrenceCount++
        $lineNumber = Get-LineNumberAtOffset $readmeText $packageReference.Index
        $versionMatches = [regex]::Matches($attributes, '(?i)\bVersion\s*=\s*["''](?<version>[^"'']+)["'']')
        if ($versionMatches.Count -ne 1)
        {
            Fail "Install command mismatch: '$readmeRelativePath' line $lineNumber (PackageReference) must contain exactly one release version '$Version'."
        }

        $installVersion = $versionMatches[0].Groups["version"].Value
        if ($installVersion -ne $Version)
        {
            Fail "Install command/version mismatch: '$readmeRelativePath' line $lineNumber (PackageReference) names '$installVersion', expected '$Version'."
        }
    }

    if ($installOccurrenceCount -eq 0)
    {
        Fail "Install command mismatch: '$readmeRelativePath' must contain a versioned install command for KeelMatrix.MetricBudget."
    }
}

Write-Output "RELEASE_VALIDATION=PASS version=$Version"
