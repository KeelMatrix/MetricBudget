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
        Get-ChildItem -LiteralPath $workflowDirectory -File -Filter "*.yml"
        Get-ChildItem -LiteralPath $workflowDirectory -File -Filter "*.yaml"
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

function Get-LeadingIndent {
    param([Parameter(Mandatory = $true)][AllowEmptyString()][string] $Line)

    return ($Line.Length - $Line.TrimStart(' ').Length)
}

function Test-QuotedValue {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyString()][string] $Value,
        [Parameter(Mandatory = $true)][string] $FileName,
        [Parameter(Mandatory = $true)][int] $LineNumber
    )

    $singleQuotes = 0
    $doubleQuotes = 0
    $escaped = $false
    for ($index = 0; $index -lt $Value.Length; $index++)
    {
        $character = $Value[$index]
        if ($character -eq "'" -and -not $escaped)
        {
            $singleQuotes++
        }
        elseif ($character -eq '"' -and -not $escaped)
        {
            $doubleQuotes++
        }

        if ($character -eq '\\' -and -not $escaped)
        {
            $escaped = $true
        }
        else
        {
            $escaped = $false
        }
    }

    if (($singleQuotes % 2) -ne 0 -or ($doubleQuotes % 2) -ne 0)
    {
        Fail "Could not parse '$FileName' line ${LineNumber}: unmatched quote."
    }
}

function Test-YamlLikeDocument {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyString()][string[]] $Lines,
        [Parameter(Mandatory = $true)][string] $FileName
    )

    $stepsIndent = $null
    $checkedStepCount = 0
    $blockScalarIndent = $null
    $blockScalarIsRun = $false

    for ($index = 0; $index -lt $Lines.Count; $index++)
    {
        $lineNumber = $index + 1
        $line = $Lines[$index]
        if ($line -match "`t")
        {
            Fail "Could not parse '$FileName' line ${lineNumber}: tabs are not valid indentation."
        }

        $trimmed = $line.Trim()
        $indent = Get-LeadingIndent $line
        if ($null -ne $blockScalarIndent)
        {
            if ($blockScalarIsRun -and $line -match '\$\{\{')
            {
                Fail "Untrusted expression found inside run body in '$FileName' line $lineNumber."
            }
            if ($trimmed.Length -eq 0 -or $indent -gt $blockScalarIndent)
            {
                continue
            }
            $blockScalarIndent = $null
            $blockScalarIsRun = $false
        }

        if ($trimmed.Length -eq 0 -or $trimmed.StartsWith('#'))
        {
            continue
        }

        if (-not $trimmed.StartsWith('-') -and $trimmed -match '^(?<key>[^:#][^:]*):\s*(?<value>.*)$')
        {
            $key = $Matches.key.Trim()
            $value = $Matches.value.Trim()
            if ($key.Length -eq 0)
            {
                Fail "Could not parse '$FileName' line ${lineNumber}: empty mapping key."
            }

            Test-QuotedValue $value $FileName $lineNumber
            if ($value -match '^(?<indicator>[|>])(?<chomp>[+-]?)(?<indentHint>\d*)\s*(?:#.*)?$')
            {
                $blockScalarIndent = $indent + 1
                $blockScalarIsRun = $key -eq 'run'
            }

            if ($key -eq 'steps' -and $value.Length -eq 0)
            {
                $stepsIndent = $indent
            }
            elseif ($null -ne $stepsIndent -and $indent -le $stepsIndent -and $key -ne 'steps')
            {
                $stepsIndent = $null
            }

            if ($key -eq 'run')
            {
                $inlineRun = $value -replace '^(?:[|>])[+-]?\s*', ''
                if ($inlineRun -match '\$\{\{')
                {
                    Fail "Untrusted expression found inside run body in '$FileName' line $lineNumber."
                }
            }

            if ($key -eq 'uses')
            {
                $usesValue = $value -replace '\s+#.*$', ''
                if ($usesValue -notmatch '^[^\s#]+@[0-9a-fA-F]{40}$')
                {
                    Fail "Unpinned or malformed uses value in '$FileName' line ${lineNumber}: '$usesValue'."
                }
            }

            continue
        }

        if ($trimmed -match '^-(?:\s+(?<entry>.*))?$')
        {
            $entry = $Matches.entry.Trim()
            Test-QuotedValue $entry $FileName $lineNumber
            if ($null -ne $stepsIndent -and $indent -gt $stepsIndent -and $indent -eq ($stepsIndent + 2))
            {
                [void]$checkedStepCount++
            }

            if ($entry -match '^(?<key>[^:#][^:]*):\s*(?<value>.*)$')
            {
                $key = $Matches.key.Trim()
                $value = $Matches.value.Trim()
                Test-QuotedValue $value $FileName $lineNumber
                if ($key -eq 'run')
                {
                    $inlineRun = $value -replace '^(?:[|>])[+-]?\s*', ''
                    if ($inlineRun -match '\$\{\{')
                    {
                        Fail "Untrusted expression found inside run body in '$FileName' line $lineNumber."
                    }
                }
                elseif ($key -eq 'uses')
                {
                    $usesValue = $value -replace '\s+#.*$', ''
                    if ($usesValue -notmatch '^[^\s#]+@[0-9a-fA-F]{40}$')
                    {
                        Fail "Unpinned or malformed uses value in '$FileName' line ${lineNumber}: '$usesValue'."
                    }
                }

                if ($value -match '^(?:[|>])[+-]?\s*$')
                {
                    $blockScalarIndent = $indent + 3
                    $blockScalarIsRun = $key -eq 'run'
                }
            }

            continue
        }

        if ($trimmed -match '^\S')
        {
            Fail "Could not parse '$FileName' line ${lineNumber}: expected a YAML mapping or sequence entry."
        }
    }

    return $checkedStepCount
}

$totalStepCount = 0
$runBodyExpressions = @()
$usesCount = 0
foreach ($workflowFile in $workflowFiles)
{
    try
    {
        $lines = [IO.File]::ReadAllLines($workflowFile.FullName)
    }
    catch
    {
        Fail "Could not read workflow '$($workflowFile.Name)': $($_.Exception.Message)"
    }

    try
    {
        $totalStepCount += Test-YamlLikeDocument -Lines $lines -FileName $workflowFile.Name
    }
    catch
    {
        if ($_.Exception.Message -like 'WORKFLOW_GUARD=FAIL:*') { throw }
        Fail "Could not parse workflow '$($workflowFile.Name)': $($_.Exception.Message)"
    }

    foreach ($line in $lines)
    {
        if ($line -match '^\s*uses:\s*')
        {
            $usesCount++
        }
    }
}

Write-Output "WORKFLOW_GUARD=FILES count=$($workflowFiles.Count)"
Write-Output "WORKFLOW_GUARD=STEPS checked=$totalStepCount"
Write-Output "WORKFLOW_GUARD=PASS check=workflow-parse"
Write-Output "WORKFLOW_GUARD=PASS check=run-expression-safety"
Write-Output "WORKFLOW_GUARD=PASS check=uses-sha-pinning actions=$usesCount"
