[CmdletBinding()]
param(
    [switch] $InspectOnly,
    [string] $PackageDirectory = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$projectPath = Join-Path $repositoryRoot "src/KeelMatrix.MetricBudget/KeelMatrix.MetricBudget.csproj"
$solutionPath = Join-Path $repositoryRoot "KeelMatrix.MetricBudget.sln"
$packageConsumerProject = Join-Path $repositoryRoot "tests/KeelMatrix.MetricBudget.PackageConsumer/KeelMatrix.MetricBudget.PackageConsumer.csproj"
$sampleProject = Join-Path $repositoryRoot "samples/KeelMatrix.MetricBudget.Sample/KeelMatrix.MetricBudget.Sample.csproj"

if ([string]::IsNullOrWhiteSpace($PackageDirectory))
{
    $PackageDirectory = Join-Path $repositoryRoot "artifacts/packages"
}

$packageDirectory = [IO.Path]::GetFullPath($PackageDirectory)
$repositoryPrefix = $repositoryRoot.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$packageDirectoryIsScoped = $packageDirectory.StartsWith($repositoryPrefix, [StringComparison]::OrdinalIgnoreCase) -and
    [IO.Path]::GetFileName($packageDirectory) -eq "packages"
if (-not $packageDirectoryIsScoped)
{
    throw "Package output must be the repository's artifacts/packages directory: $packageDirectory"
}

function Format-DotnetCommand {
    param([Parameter(Mandatory = $true)][string[]] $Arguments)

    $formattedArguments = $Arguments | ForEach-Object {
        if ($_ -match '[\s"]')
        {
            '"' + $_.Replace('"', '\"') + '"'
        }
        else
        {
            $_
        }
    }

    return "dotnet " + ($formattedArguments -join " ")
}

function Get-BoundedTail {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyString()][string] $Text,
        [Parameter(Mandatory = $true)][int] $MaximumLines
    )

    $lines = @($Text -split "`r?`n")
    if ($lines.Count -le $MaximumLines)
    {
        return $Text.TrimEnd()
    }

    return "[output truncated; showing the last $MaximumLines lines]`n" +
        (($lines | Select-Object -Last $MaximumLines) -join "`n").TrimEnd()
}

function Invoke-Dotnet {
    param(
        [Parameter(Mandatory = $true)][string] $Step,
        [Parameter(Mandatory = $true)][string[]] $Arguments,
        [switch] $AllowFailure
    )

    $commandText = Format-DotnetCommand $Arguments
    Write-Host "> $commandText"

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = "dotnet"
    $startInfo.WorkingDirectory = $repositoryRoot
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in $Arguments)
    {
        [void]$startInfo.ArgumentList.Add($argument)
    }

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try
    {
        [void]$process.Start()
        $standardOutputTask = $process.StandardOutput.ReadToEndAsync()
        $standardErrorTask = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $standardOutput = $standardOutputTask.GetAwaiter().GetResult()
        $standardError = $standardErrorTask.GetAwaiter().GetResult()
        $exitCode = $process.ExitCode
    }
    finally
    {
        $process.Dispose()
    }

    if ($standardOutput.Length -gt 0)
    {
        Write-Host $standardOutput.TrimEnd()
    }
    if ($standardError.Length -gt 0)
    {
        Write-Host $standardError.TrimEnd()
    }

    $result = [pscustomobject]@{
        Step = $Step
        Command = $commandText
        Arguments = $Arguments
        ExitCode = $exitCode
        StandardOutput = $standardOutput
        StandardError = $standardError
        CombinedOutput = $standardOutput + $standardError
    }

    if ($exitCode -ne 0 -and -not $AllowFailure)
    {
        $stdoutTail = Get-BoundedTail $standardOutput 80
        $stderrTail = Get-BoundedTail $standardError 80
        Write-Host "FAILURE: gate step '$Step' failed."
        Write-Host "Command: $commandText"
        Write-Host "Exit code: $exitCode"
        Write-Host "Captured standard output (last 80 lines maximum):"
        Write-Host $(if ($stdoutTail.Length -gt 0) { $stdoutTail } else { "[empty]" })
        Write-Host "Captured standard error (last 80 lines maximum):"
        Write-Host $(if ($stderrTail.Length -gt 0) { $stderrTail } else { "[empty]" })
        throw "Gate step '$Step' failed: $commandText exited with code $exitCode."
    }

    if ($exitCode -ne 0 -and $AllowFailure)
    {
        $stdoutTail = Get-BoundedTail $standardOutput 80
        $stderrTail = Get-BoundedTail $standardError 80
        Write-Host "FAILURE: gate step '$Step' failed; the caller may classify this attempt."
        Write-Host "Command: $commandText"
        Write-Host "Exit code: $exitCode"
        Write-Host "Captured standard output (last 80 lines maximum):"
        Write-Host $(if ($stdoutTail.Length -gt 0) { $stdoutTail } else { "[empty]" })
        Write-Host "Captured standard error (last 80 lines maximum):"
        Write-Host $(if ($stderrTail.Length -gt 0) { $stderrTail } else { "[empty]" })
    }

    return $result
}

function Assert-Equal {
    param(
        [Parameter(Mandatory = $true)] $Actual,
        [Parameter(Mandatory = $true)] $Expected,
        [Parameter(Mandatory = $true)][string] $Message
    )

    if ($Actual -is [Array] -or $Expected -is [Array])
    {
        $actualText = (@($Actual) -join "`n")
        $expectedText = (@($Expected) -join "`n")
        if ($actualText -ne $expectedText)
        {
            throw "$Message`nExpected:`n$expectedText`nActual:`n$actualText"
        }

        return
    }

    if ($Actual -ne $Expected)
    {
        throw "$Message Expected '$Expected'; actual '$Actual'."
    }
}

function Assert-ContainsExactly {
    param(
        [Parameter(Mandatory = $true)][string[]] $Actual,
        [Parameter(Mandatory = $true)][string[]] $Expected,
        [Parameter(Mandatory = $true)][string] $Message
    )

    $actualSorted = @($Actual | Sort-Object)
    $expectedSorted = @($Expected | Sort-Object)
    Assert-Equal $actualSorted $expectedSorted $Message
}

function Get-ZipEntries {
    param([Parameter(Mandatory = $true)][string] $Path)

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($Path)
    try
    {
        return @($archive.Entries | ForEach-Object { $_.FullName })
    }
    finally
    {
        $archive.Dispose()
    }
}

function Read-ZipEntryText {
    param(
        [Parameter(Mandatory = $true)][string] $ArchivePath,
        [Parameter(Mandatory = $true)][string] $EntryName
    )

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($ArchivePath)
    try
    {
        $entry = $archive.GetEntry($EntryName)
        if ($null -eq $entry)
        {
            throw "Archive '$ArchivePath' is missing '$EntryName'."
        }

        $reader = [IO.StreamReader]::new($entry.Open(), [Text.Encoding]::UTF8, $true)
        try
        {
            return $reader.ReadToEnd()
        }
        finally
        {
            $reader.Dispose()
        }
    }
    finally
    {
        $archive.Dispose()
    }
}

function Read-ZipEntryBytes {
    param(
        [Parameter(Mandatory = $true)][string] $ArchivePath,
        [Parameter(Mandatory = $true)][string] $EntryName
    )

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($ArchivePath)
    try
    {
        $entry = $archive.GetEntry($EntryName)
        if ($null -eq $entry)
        {
            throw "Archive '$ArchivePath' is missing '$EntryName'."
        }

        $stream = $entry.Open()
        try
        {
            $memory = [IO.MemoryStream]::new()
            try
            {
                $stream.CopyTo($memory)
                return $memory.ToArray()
            }
            finally
            {
                $memory.Dispose()
            }
        }
        finally
        {
            $stream.Dispose()
        }
    }
    finally
    {
        $archive.Dispose()
    }
}

function Get-NuspecMetadata {
    param([Parameter(Mandatory = $true)][string] $ArchivePath)

    $nuspecText = Read-ZipEntryText $ArchivePath "KeelMatrix.MetricBudget.nuspec"
    $document = [Xml]$nuspecText
    $metadata = $document.SelectSingleNode("/*[local-name()='package']/*[local-name()='metadata']")
    if ($null -eq $metadata)
    {
        throw "Nuspec metadata is missing."
    }

    return $metadata
}

function Get-NuspecValue {
    param(
        [Parameter(Mandatory = $true)] $Metadata,
        [Parameter(Mandatory = $true)][string] $Name
    )

    $node = $null
    foreach ($child in $Metadata.ChildNodes)
    {
        if ($child.LocalName -eq $Name)
        {
            $node = $child
            break
        }
    }
    if ($null -eq $node)
    {
        throw "Nuspec metadata is missing '$Name'."
    }

    return $node.InnerText
}

function Get-PngUInt32 {
    param(
        [Parameter(Mandatory = $true)][byte[]] $Bytes,
        [Parameter(Mandatory = $true)][int] $Offset
    )

    return ([uint32]$Bytes[$Offset] -shl 24) -bor ([uint32]$Bytes[$Offset + 1] -shl 16) -bor
        ([uint32]$Bytes[$Offset + 2] -shl 8) -bor [uint32]$Bytes[$Offset + 3]
}

function Assert-PackageMetadata {
    param(
        [Parameter(Mandatory = $true)][string] $ArchivePath,
        [Parameter(Mandatory = $true)][string] $ExpectedCommit
    )

    $metadata = Get-NuspecMetadata $ArchivePath
    Assert-Equal (Get-NuspecValue $metadata "id") "KeelMatrix.MetricBudget" "Package id mismatch."
    Assert-Equal (Get-NuspecValue $metadata "version") "0.1.0" "Package version mismatch."
    Assert-Equal (Get-NuspecValue $metadata "authors") "KeelMatrix" "Package authors mismatch."
    Assert-Equal (Get-NuspecValue $metadata "license") "MIT" "Package license mismatch."
    Assert-Equal (Get-NuspecValue $metadata "description") "Verify observed metric cardinality in .NET tests and CI. Observe the metric series the exercised workload actually emits, count the distinct values each tag key produced, and fail when an instrument exceeds an explicit observed-series or per-tag distinct-value budget." "Package description mismatch."
    Assert-Equal (Get-NuspecValue $metadata "tags") "metrics cardinality opentelemetry system-diagnostics-metrics testing ci observability" "Package tags mismatch."
    Assert-Equal (Get-NuspecValue $metadata "readme") "README.md" "Package README metadata mismatch."
    Assert-Equal (Get-NuspecValue $metadata "icon") "icon.png" "Package icon metadata mismatch."
    Assert-Equal (Get-NuspecValue $metadata "projectUrl") "https://github.com/KeelMatrix/MetricBudget#readme" "Package project URL mismatch."
    $repository = $metadata.SelectSingleNode("./*[local-name()='repository']")
    if ($null -eq $repository)
    {
        throw "Package repository metadata is missing."
    }

    Assert-Equal $repository.GetAttribute("type") "git" "Repository type mismatch."
    Assert-Equal $repository.GetAttribute("url") "https://github.com/KeelMatrix/MetricBudget" "Repository URL mismatch."
    Assert-Equal $repository.GetAttribute("branch") "refs/heads/main" "Repository branch mismatch."
    Assert-Equal $repository.GetAttribute("commit") $ExpectedCommit "Repository commit mismatch."

    $dependencies = $metadata.SelectSingleNode("./*[local-name()='dependencies']")
    if ($null -eq $dependencies)
    {
        throw "Package dependency metadata is missing."
    }

    $groups = @($dependencies.SelectNodes("./*[local-name()='group']"))
    if ($groups.Count -ne 2)
    {
        throw "Expected exactly two target-framework dependency groups; found $($groups.Count)."
    }

    foreach ($group in $groups)
    {
        $targetFramework = $group.GetAttribute("targetFramework")
        $dependencyNodes = @($group.SelectNodes("./*[local-name()='dependency']"))
        $actualDependencies = @($dependencyNodes | ForEach-Object {
                $_.GetAttribute("id") + " " + $_.GetAttribute("version")
            } | Sort-Object)

        if ($targetFramework -ieq "net8.0")
        {
            Assert-ContainsExactly $actualDependencies @("KeelMatrix.Telemetry [0.1.0]") "net8.0 dependency set mismatch."
        }
        elseif ($targetFramework -ieq ".NETStandard2.0" -or $targetFramework -ieq "netstandard2.0")
        {
            Assert-ContainsExactly $actualDependencies @(
                "KeelMatrix.Telemetry [0.1.0]",
                "System.Diagnostics.DiagnosticSource 8.0.1") "netstandard2.0 dependency set mismatch."
        }
        else
        {
            throw "Unexpected target-framework dependency group '$targetFramework'."
        }
    }
}

function Assert-SymbolPackageMetadata {
    param(
        [Parameter(Mandatory = $true)][string] $ArchivePath,
        [Parameter(Mandatory = $true)][string] $ExpectedCommit
    )

    $metadata = Get-NuspecMetadata $ArchivePath
    Assert-Equal (Get-NuspecValue $metadata "id") "KeelMatrix.MetricBudget" "Symbols package id mismatch."
    Assert-Equal (Get-NuspecValue $metadata "version") "0.1.0" "Symbols package version mismatch."
    Assert-Equal (Get-NuspecValue $metadata "projectUrl") "https://github.com/KeelMatrix/MetricBudget#readme" "Symbols package project URL mismatch."
    Assert-Equal (Get-NuspecValue $metadata "description") "Verify observed metric cardinality in .NET tests and CI. Observe the metric series the exercised workload actually emits, count the distinct values each tag key produced, and fail when an instrument exceeds an explicit observed-series or per-tag distinct-value budget." "Symbols package description mismatch."
    Assert-Equal (Get-NuspecValue $metadata "tags") "metrics cardinality opentelemetry system-diagnostics-metrics testing ci observability" "Symbols package tags mismatch."

    $packageType = $metadata.SelectSingleNode("./*[local-name()='packageTypes']/*[local-name()='packageType']")
    if ($null -eq $packageType)
    {
        throw "Symbols package type metadata is missing."
    }

    Assert-Equal $packageType.GetAttribute("name") "SymbolsPackage" "Symbols package type mismatch."
    $repository = $metadata.SelectSingleNode("./*[local-name()='repository']")
    if ($null -eq $repository)
    {
        throw "Symbols package repository metadata is missing."
    }

    Assert-Equal $repository.GetAttribute("type") "git" "Symbols repository type mismatch."
    Assert-Equal $repository.GetAttribute("url") "https://github.com/KeelMatrix/MetricBudget" "Symbols repository URL mismatch."
    Assert-Equal $repository.GetAttribute("branch") "refs/heads/main" "Symbols repository branch mismatch."
    Assert-Equal $repository.GetAttribute("commit") $ExpectedCommit "Symbols repository commit mismatch."
}

function Assert-ArchiveSet {
    param(
        [Parameter(Mandatory = $true)][string] $ArchivePath,
        [Parameter(Mandatory = $true)][string[]] $ExpectedEntries,
        [Parameter(Mandatory = $true)][string] $Label
    )

    if (-not (Test-Path -LiteralPath $ArchivePath -PathType Leaf))
    {
        throw "$Label archive is missing: $ArchivePath"
    }

    Assert-ContainsExactly (Get-ZipEntries $ArchivePath) $ExpectedEntries "$Label contains an unexpected or missing entry."
}

function Assert-Icon {
    param([Parameter(Mandatory = $true)][string] $ArchivePath)

    $icon = Read-ZipEntryBytes $ArchivePath "icon.png"
    $validPng = $icon.Length -ge 24
    $validPng = $validPng -and $icon[0] -eq 137 -and $icon[1] -eq 80 -and $icon[2] -eq 78 -and $icon[3] -eq 71
    $validPng = $validPng -and $icon[4] -eq 13 -and $icon[5] -eq 10 -and $icon[6] -eq 26 -and $icon[7] -eq 10
    if (-not $validPng)
    {
        throw "Package icon is not a valid PNG."
    }

    $width = Get-PngUInt32 $icon 16
    $height = Get-PngUInt32 $icon 20
    Assert-Equal $width 512 "Package icon width mismatch."
    Assert-Equal $height 512 "Package icon height mismatch."
    if ($icon.Length -gt 200KB)
    {
        throw "Package icon exceeds the 200 KB limit: $($icon.Length) bytes."
    }
}

function Assert-SourceBytes {
    param([Parameter(Mandatory = $true)][string] $ArchivePath)

    $readme = Read-ZipEntryBytes $ArchivePath "README.md"
    $sourceReadme = [IO.File]::ReadAllBytes((Join-Path $repositoryRoot "src/KeelMatrix.MetricBudget/README.md"))
    Assert-Equal ([Convert]::ToBase64String($readme)) ([Convert]::ToBase64String($sourceReadme)) "Package README differs from the shipping project README."

    $icon = Read-ZipEntryBytes $ArchivePath "icon.png"
    $sourceIcon = [IO.File]::ReadAllBytes((Join-Path $repositoryRoot "icon.png"))
    Assert-Equal ([Convert]::ToBase64String($icon)) ([Convert]::ToBase64String($sourceIcon)) "Package icon differs from the repository icon."
}

function Assert-RootSources {
    $configPath = Join-Path $repositoryRoot "NuGet.config"
    $config = [Xml](Get-Content -Raw $configPath)
    $sources = @($config.SelectNodes("/*[local-name()='configuration']/*[local-name()='packageSources']/*[local-name()='add']"))
    if ($sources.Count -ne 1 -or $sources[0].GetAttribute("key") -ne "nuget.org")
    {
        throw "The solution restore sources are not explicitly limited to nuget.org."
    }
}

function Invoke-CleanConsumerProof {
    $scratch = Join-Path ([IO.Path]::GetTempPath()) ("KeelMatrix.MetricBudget-package-gate-" + [Guid]::NewGuid().ToString("N"))
    $packages = Join-Path $scratch "packages"
    $httpCache = Join-Path $scratch "http-cache"
    New-Item -ItemType Directory -Path $packages -Force | Out-Null
    New-Item -ItemType Directory -Path $httpCache -Force | Out-Null

    $oldPackages = $env:NUGET_PACKAGES
    $oldHttpCache = $env:NUGET_HTTP_CACHE_PATH
    $oldFallback = $env:NUGET_FALLBACK_PACKAGES
    $oldBuildServers = $env:DOTNET_CLI_DISABLE_BUILD_SERVERS
    try
    {
        $env:NUGET_PACKAGES = $packages
        $env:NUGET_HTTP_CACHE_PATH = $httpCache
        $env:NUGET_FALLBACK_PACKAGES = ""
        $env:DOTNET_CLI_DISABLE_BUILD_SERVERS = "1"
        $cleanBuildProperties = @("-p:UseSharedCompilation=false", "-p:MSBuildNodeReuse=false")

        foreach ($project in @($packageConsumerProject, $sampleProject))
        {
            $obj = Join-Path (Split-Path -Parent $project) "obj"
            if (Test-Path -LiteralPath $obj)
            {
                Remove-Item -LiteralPath $obj -Recurse -Force
            }
        }

        $solutionRestoreArguments = @(
            "restore", $solutionPath, "--configfile", (Join-Path $repositoryRoot "NuGet.config"),
            "--force-evaluate", "--no-cache", "--disable-build-servers") + $cleanBuildProperties
        Invoke-Dotnet -Step "Clean solution restore" -Arguments $solutionRestoreArguments | Out-Null
        Invoke-Dotnet -Step "Clean solution Release build" -Arguments (@("build", $solutionPath, "-c", "Release", "--no-restore", "--disable-build-servers") + $cleanBuildProperties) | Out-Null

        foreach ($project in @($packageConsumerProject, $sampleProject))
        {
            $projectDirectory = Split-Path -Parent $project
            $restoreArguments = @(
                "restore", $project, "--configfile", (Join-Path $projectDirectory "NuGet.config"),
                "--force-evaluate", "--no-cache", "--disable-build-servers") + $cleanBuildProperties
            $projectName = Split-Path -Leaf $projectDirectory
            Invoke-Dotnet -Step "Clean $projectName restore" -Arguments $restoreArguments | Out-Null
            Invoke-Dotnet -Step "Clean $projectName Release build" -Arguments (@("build", $project, "-c", "Release", "--no-restore", "--disable-build-servers") + $cleanBuildProperties) | Out-Null
            Invoke-Dotnet -Step "Clean $projectName smoke run" -Arguments (@("run", "--project", $project, "-c", "Release", "--no-build", "--no-restore", "--disable-build-servers") + $cleanBuildProperties) | Out-Null
        }

        $cacheContents = @(Get-ChildItem -LiteralPath $packages -Force -ErrorAction SilentlyContinue)
        if ($cacheContents.Count -eq 0)
        {
            throw "The clean package cache stayed empty; the consumer proof did not restore dependencies."
        }

        Write-Output "CLEAN_CACHE_PROOF=PASS"
    }
    finally
    {
        if ($null -eq $oldPackages) { Remove-Item Env:NUGET_PACKAGES -ErrorAction SilentlyContinue } else { $env:NUGET_PACKAGES = $oldPackages }
        if ($null -eq $oldHttpCache) { Remove-Item Env:NUGET_HTTP_CACHE_PATH -ErrorAction SilentlyContinue } else { $env:NUGET_HTTP_CACHE_PATH = $oldHttpCache }
        if ($null -eq $oldFallback) { Remove-Item Env:NUGET_FALLBACK_PACKAGES -ErrorAction SilentlyContinue } else { $env:NUGET_FALLBACK_PACKAGES = $oldFallback }
        if ($null -eq $oldBuildServers) { Remove-Item Env:DOTNET_CLI_DISABLE_BUILD_SERVERS -ErrorAction SilentlyContinue } else { $env:DOTNET_CLI_DISABLE_BUILD_SERVERS = $oldBuildServers }
        if (Test-Path -LiteralPath $scratch)
        {
            $removed = $false
            for ($attempt = 1; $attempt -le 3 -and -not $removed; $attempt++)
            {
                try
                {
                    Remove-Item -LiteralPath $scratch -Recurse -Force
                    $removed = $true
                }
                catch
                {
                    if ($attempt -lt 3) { Start-Sleep -Milliseconds 250 }
                }
            }

            if (-not $removed)
            {
                throw "Could not clean the task-local cache directory '$scratch'."
            }
        }
    }
}

function Test-VulnerabilityResult {
    param([Parameter(Mandatory = $true)][AllowEmptyString()][string] $Output)

    return $Output -match "(?im)has the following vulnerable packages|known vulnerability|severity\s*[:|]\s*(critical|high|moderate|low)"
}

function Test-AdvisoryNetworkFailure {
    param([Parameter(Mandatory = $true)][AllowEmptyString()][string] $Output)

    return $Output -match "(?im)NU1301|NU1801|NU1900|unable to load the service index|vulnerability data|advisory service|timed? ?out|timeout|temporary failure|name resolution|connection (?:refused|reset|closed)|service unavailable|\b(?:502|503|504)\b|network"
}

function Invoke-VulnerabilityAudit {
    $auditArguments = @("list", $solutionPath, "package", "--vulnerable", "--include-transitive", "--no-restore")
    $firstAttempt = Invoke-Dotnet -Step "Vulnerability audit (attempt 1)" -Arguments $auditArguments -AllowFailure
    Write-Host "VULNERABILITY_AUDIT_ATTEMPT=1 EXIT_CODE=$($firstAttempt.ExitCode)"

    if (Test-VulnerabilityResult $firstAttempt.CombinedOutput)
    {
        Write-Host "VULNERABILITY_AUDIT=FAIL attempt=1 retry=not-attempted vulnerability-result=true"
        throw "The dependency audit reported a vulnerability on attempt 1; no retry was attempted."
    }

    $firstAttemptIsNetworkFailure = Test-AdvisoryNetworkFailure $firstAttempt.CombinedOutput
    if (($firstAttempt.ExitCode -eq 0) -and -not $firstAttemptIsNetworkFailure)
    {
        Write-Host "VULNERABILITY_AUDIT=PASS attempt=1"
        return
    }

    if (-not $firstAttemptIsNetworkFailure)
    {
        Write-Host "VULNERABILITY_AUDIT=FAIL attempt=1 retry=not-attempted network-failure=false"
        throw "Vulnerability audit failed on attempt 1 without a recognized advisory-service or network failure."
    }

    Write-Host "VULNERABILITY_AUDIT_RETRY=1 reason=recognized-advisory-service-or-network-failure"
    $secondAttempt = Invoke-Dotnet -Step "Vulnerability audit (attempt 2)" -Arguments $auditArguments -AllowFailure
    Write-Host "VULNERABILITY_AUDIT_ATTEMPT=2 EXIT_CODE=$($secondAttempt.ExitCode)"

    if (Test-VulnerabilityResult $secondAttempt.CombinedOutput)
    {
        Write-Host "VULNERABILITY_AUDIT=FAIL attempt=2 retry=exhausted vulnerability-result=true"
        throw "The dependency audit reported a vulnerability on attempt 2."
    }

    $secondAttemptIsNetworkFailure = Test-AdvisoryNetworkFailure $secondAttempt.CombinedOutput
    if ($secondAttempt.ExitCode -ne 0 -or $secondAttemptIsNetworkFailure)
    {
        Write-Host "VULNERABILITY_AUDIT=FAIL attempt=2 retry=exhausted network-or-advisory-failure=true"
        throw "The dependency audit failed after the one permitted retry."
    }

    Write-Host "VULNERABILITY_AUDIT=PASS attempt=2 after-one-retry"
}

if (-not $InspectOnly)
{
    if (-not (Test-Path -LiteralPath $packageDirectory))
    {
        New-Item -ItemType Directory -Path $packageDirectory -Force | Out-Null
    }

    foreach ($child in @(Get-ChildItem -LiteralPath $packageDirectory -Force))
    {
        Remove-Item -LiteralPath $child.FullName -Recurse -Force
    }

    $restoreArguments = @(
        "restore", $projectPath, "--configfile", (Join-Path $repositoryRoot "NuGet.config"),
        "--force-evaluate", "--no-cache", "--disable-build-servers"
    )
    Invoke-Dotnet -Step "Package project restore" -Arguments $restoreArguments | Out-Null
    Invoke-Dotnet -Step "Release package build" -Arguments @("pack", $projectPath, "-c", "Release", "-o", $packageDirectory, "--no-restore") | Out-Null
}
else
{
    Write-Output "INSPECT_ONLY=true"
}

$expectedNupkgEntries = @(
    "[Content_Types].xml",
    "_rels/.rels",
    "KeelMatrix.MetricBudget.nuspec",
    "README.md",
    "icon.png",
    "lib/net8.0/KeelMatrix.MetricBudget.dll",
    "lib/net8.0/KeelMatrix.MetricBudget.xml",
    "lib/netstandard2.0/KeelMatrix.MetricBudget.dll",
    "lib/netstandard2.0/KeelMatrix.MetricBudget.xml",
    "package/services/metadata/core-properties/nuget.psmdcp"
)
$expectedSnupkgEntries = @(
    "[Content_Types].xml",
    "_rels/.rels",
    "KeelMatrix.MetricBudget.nuspec",
    "lib/net8.0/KeelMatrix.MetricBudget.pdb",
    "lib/netstandard2.0/KeelMatrix.MetricBudget.pdb",
    "package/services/metadata/core-properties/nuget.psmdcp"
)

$nupkg = Join-Path $packageDirectory "KeelMatrix.MetricBudget.0.1.0.nupkg"
$snupkg = Join-Path $packageDirectory "KeelMatrix.MetricBudget.0.1.0.snupkg"
Assert-ArchiveSet $nupkg $expectedNupkgEntries ".nupkg"
Assert-ArchiveSet $snupkg $expectedSnupkgEntries ".snupkg"
Assert-Equal @((Get-ChildItem -LiteralPath $packageDirectory -File | Sort-Object Name | ForEach-Object Name)) @(
    "KeelMatrix.MetricBudget.0.1.0.nupkg",
    "KeelMatrix.MetricBudget.0.1.0.snupkg") "Package directory contains an unexpected artifact."

$expectedCommit = (git -C $repositoryRoot rev-parse HEAD).Trim()
Assert-PackageMetadata $nupkg $expectedCommit
Assert-SymbolPackageMetadata $snupkg $expectedCommit
Assert-Icon $nupkg
Assert-SourceBytes $nupkg
Write-Output "ARCHIVE_INSPECTION=PASS"

if (-not $InspectOnly)
{
    Assert-RootSources
    Invoke-CleanConsumerProof
    Invoke-VulnerabilityAudit
}

Write-Output "PACKAGE_GATE=PASS"
