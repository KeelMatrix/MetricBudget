[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$workflowDirectory = Join-Path $repositoryRoot ".github/workflows"
$minimumWorkflowCount = 2

function Fail {
    param([Parameter(Mandatory = $true)][string] $Message)

    throw "WORKFLOW_GUARD=FAIL: $Message"
}

if (-not (Test-Path -LiteralPath $workflowDirectory -PathType Container))
{
    Fail "Workflow directory '$workflowDirectory' is missing."
}

try
{
    $workflowFiles = @(
        Get-ChildItem -LiteralPath $workflowDirectory -File -Filter "*.yml" -Recurse -Force
        Get-ChildItem -LiteralPath $workflowDirectory -File -Filter "*.yaml" -Recurse -Force
    ) | Sort-Object FullName -Unique
}
catch
{
    Fail "Could not enumerate workflow files: $($_.Exception.Message)"
}

if ($workflowFiles.Count -lt $minimumWorkflowCount)
{
    Fail "Expected at least $minimumWorkflowCount workflow files, but found $($workflowFiles.Count)."
}

function Get-WorkflowRelativePath {
    param([Parameter(Mandatory = $true)][string] $Path)

    return [IO.Path]::GetRelativePath($workflowDirectory, $Path).Replace('\', '/')
}

function Get-LeadingIndent {
    param([Parameter(Mandatory = $true)][AllowEmptyString()][string] $Line)

    return ($Line.Length - $Line.TrimStart(' ').Length)
}

function Normalize-YamlKey {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyString()][string] $RawKey,
        [Parameter(Mandatory = $true)][string] $FileName,
        [Parameter(Mandatory = $true)][int] $LineNumber
    )

    $key = $RawKey.Trim()
    if ($key.Length -ge 2 -and (($key[0] -eq "'" -and $key[$key.Length - 1] -eq "'") -or ($key[0] -eq '"' -and $key[$key.Length - 1] -eq '"')))
    {
        $key = $key.Substring(1, $key.Length - 2)
    }
    elseif ($key.StartsWith("'") -or $key.StartsWith('"') -or $key.EndsWith("'") -or $key.EndsWith('"'))
    {
        Fail "Could not classify '$FileName' line ${LineNumber}: malformed quoted YAML key '$RawKey'."
    }

    $key = $key.Trim()
    if ($key.Length -eq 0 -or $key -notmatch '^[A-Za-z0-9_.-]+$')
    {
        Fail "Could not classify '$FileName' line ${LineNumber}: unusual YAML key '$RawKey'."
    }

    return $key
}

function Test-ScalarValue {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyString()][string] $Value,
        [Parameter(Mandatory = $true)][string] $FileName,
        [Parameter(Mandatory = $true)][int] $LineNumber
    )

    $value = $Value.Trim()
    if ($value.Length -eq 0)
    {
        return "Empty"
    }

    if ($value.StartsWith('{') -or $value.StartsWith('['))
    {
        Fail "Could not classify '$FileName' line ${LineNumber}: flow-style YAML value is not supported."
    }

    if ($value -match '^[|>](?:(?:[+-]\d*)|(?:\d*[+-]?))(?:\s+#.*)?$')
    {
        return "Block"
    }

    if ($value.StartsWith('&') -or $value.StartsWith('*') -or $value.StartsWith('!') -or $value.StartsWith('?'))
    {
        Fail "Could not classify '$FileName' line ${LineNumber}: YAML anchor, alias, tag, or explicit-key value is not supported."
    }

    if ($value.StartsWith("'"))
    {
        if ($value -notmatch '^''(?:[^'']|'''')*''(?:\s+#.*)?$')
        {
            Fail "Could not classify '$FileName' line ${LineNumber}: malformed single-quoted scalar."
        }

        return "Quoted"
    }

    if ($value.StartsWith('"'))
    {
        if ($value -notmatch '^"(?:[^"\\]|\\.)*"(?:\s+#.*)?$')
        {
            Fail "Could not classify '$FileName' line ${LineNumber}: malformed double-quoted scalar."
        }

        return "Quoted"
    }

    if ($value -match '(?<!\\)#(?=\S)' -and $value -notmatch '\s+#')
    {
        Fail "Could not classify '$FileName' line ${LineNumber}: unrecognized comment placement in scalar."
    }

    return "Plain"
}

$mappingPattern = '^(?<prefix>-\s*)?(?<rawKey>"(?:[^"\\]|\\.)*"|''(?:[^'']|'''')*''|[^\s:#][^:]*?)\s*:\s*(?<value>.*)$'
function Get-MappingLine {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyString()][string] $TrimmedLine,
        [Parameter(Mandatory = $true)][string] $FileName,
        [Parameter(Mandatory = $true)][int] $LineNumber
    )

    $match = [regex]::Match($TrimmedLine, $mappingPattern)
    if (-not $match.Success)
    {
        return $null
    }

    $key = Normalize-YamlKey $match.Groups["rawKey"].Value $FileName $LineNumber
    $value = $match.Groups["value"].Value.Trim()
    $valueKind = Test-ScalarValue $value $FileName $LineNumber
    return [pscustomobject]@{
        IsSequenceEntry = $match.Groups["prefix"].Success
        Key = $key
        Value = $value
        ValueKind = $valueKind
    }
}

# This is intentionally a separate, line-local pass. It does not consult the
# indentation/state parser below, so a second independent mistake is needed
# to put an expression into a shell body or another executable scalar.
$expressionKeyAllowlist = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($key in @(
        "if", "name", "runs-on", "group", "env", "with", "path", "version", "timeout-minutes",
        "RELEASE_EVENT_NAME", "RELEASE_REF_NAME", "RELEASE_INPUT_VERSION", "RELEASE_VERSION", "NUGET_PUSH_CREDENTIAL"
    ))
{
    [void]$expressionKeyAllowlist.Add($key)
}

function Test-ExpressionSafety {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyString()][string[]] $Lines,
        [Parameter(Mandatory = $true)][string] $FileName
    )

    for ($index = 0; $index -lt $Lines.Count; $index++)
    {
        $line = $Lines[$index]
        if ($line.IndexOf('${{', [StringComparison]::Ordinal) -lt 0)
        {
            continue
        }

        $lineNumber = $index + 1
        $trimmed = $line.Trim()
        $mapping = Get-MappingLine $trimmed $FileName $lineNumber
        if ($null -eq $mapping)
        {
            Fail "Could not classify expression in '$FileName' line ${lineNumber}: it is not on a recognized mapping key."
        }

        if (-not $expressionKeyAllowlist.Contains($mapping.Key))
        {
            Fail "Expression in '$FileName' line ${lineNumber} uses non-shell key '$($mapping.Key)'."
        }

        if ($mapping.ValueKind -eq "Block" -or $mapping.ValueKind -eq "Empty")
        {
            Fail "Expression in '$FileName' line ${lineNumber} is not in a single-line scalar value."
        }
    }
}

function Test-YamlLikeDocument {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyString()][string[]] $Lines,
        [Parameter(Mandatory = $true)][string] $FileName
    )

    $stepsIndent = $null
    $checkedStepCount = 0
    $usesCount = 0
    $blockScalarOwnerIndent = $null

    for ($index = 0; $index -lt $Lines.Count; $index++)
    {
        $lineNumber = $index + 1
        $line = $Lines[$index]
        if ($line.IndexOf("`t", [StringComparison]::Ordinal) -ge 0)
        {
            Fail "Could not classify '$FileName' line ${lineNumber}: tabs are not valid indentation."
        }

        $trimmed = $line.Trim()
        $indent = Get-LeadingIndent $line

        if ($null -ne $blockScalarOwnerIndent)
        {
            if ($trimmed.Length -eq 0 -or $indent -gt $blockScalarOwnerIndent)
            {
                continue
            }

            $blockScalarOwnerIndent = $null
        }

        if ($trimmed.Length -eq 0 -or $trimmed.StartsWith('#'))
        {
            continue
        }

        $mapping = Get-MappingLine $trimmed $FileName $lineNumber
        if ($null -ne $mapping)
        {
            if ($mapping.IsSequenceEntry -and $null -ne $stepsIndent -and $indent -eq ($stepsIndent + 2))
            {
                [void]$checkedStepCount++
            }

            if ($null -ne $stepsIndent -and $indent -le $stepsIndent -and $mapping.Key -ne 'steps')
            {
                $stepsIndent = $null
            }

            if ($mapping.Key -eq 'steps' -and $mapping.ValueKind -eq 'Empty')
            {
                $stepsIndent = $indent
            }

            if ($mapping.Key -eq 'uses')
            {
                $usesValue = $mapping.Value -replace '\s+#.*$', ''
                if ($usesValue -notmatch '^[^\s#]+@[0-9a-fA-F]{40}$')
                {
                    Fail "Unpinned or malformed uses value in '$FileName' line ${lineNumber}: '$usesValue'."
                }

                [void]$usesCount++
            }

            if ($mapping.ValueKind -eq 'Block')
            {
                $blockScalarOwnerIndent = $indent
            }

            continue
        }

        if ($trimmed -match '^-(?:\s+(?<entry>.*))?$')
        {
            $entry = $Matches.entry.Trim()
            if ($null -ne $stepsIndent -and $indent -eq ($stepsIndent + 2))
            {
                [void]$checkedStepCount++
            }

            if ($entry.Length -eq 0)
            {
                continue
            }

            if ($entry.StartsWith('{') -or $entry.StartsWith('['))
            {
                Fail "Could not classify '$FileName' line ${lineNumber}: flow-style YAML sequence entry is not supported."
            }

            # A sequence entry with a mapping key was already handled above
            # only when the prefix is present in the full line. Any remaining
            # entry must be an ordinary, single-line scalar.
            [void](Test-ScalarValue $entry $FileName $lineNumber)
            continue
        }

        Fail "Could not classify '$FileName' line ${lineNumber}: unsupported YAML construct."
    }

    return [pscustomobject]@{
        Steps = $checkedStepCount
        Uses = $usesCount
    }
}

$totalStepCount = 0
$usesCount = 0
foreach ($workflowFile in $workflowFiles)
{
    $fileName = Get-WorkflowRelativePath $workflowFile.FullName
    try
    {
        $lines = [IO.File]::ReadAllLines($workflowFile.FullName)
    }
    catch
    {
        Fail "Could not read workflow '$fileName': $($_.Exception.Message)"
    }

    try
    {
        Test-ExpressionSafety -Lines $lines -FileName $fileName
        $result = Test-YamlLikeDocument -Lines $lines -FileName $fileName
        $totalStepCount += $result.Steps
        $usesCount += $result.Uses
    }
    catch
    {
        if ($_.Exception.Message -like 'WORKFLOW_GUARD=FAIL:*') { throw }
        Fail "Could not parse workflow '$fileName': $($_.Exception.Message)"
    }
}

Write-Output "WORKFLOW_GUARD=FILES count=$($workflowFiles.Count)"
Write-Output "WORKFLOW_GUARD=STEPS checked=$totalStepCount"
Write-Output "WORKFLOW_GUARD=PASS check=workflow-parse"
Write-Output "WORKFLOW_GUARD=PASS check=run-expression-safety"
Write-Output "WORKFLOW_GUARD=PASS check=uses-sha-pinning actions=$usesCount"
