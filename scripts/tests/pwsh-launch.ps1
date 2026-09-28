function Get-PwshChildArguments {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][bool] $HideWindow,
        [Parameter(Mandatory = $true)][string] $ScriptPath,
        [Parameter()][string[]] $ScriptArguments = @()
    )

    $arguments = @('-NoProfile')
    if ($HideWindow) {
        $arguments += @('-WindowStyle', 'Hidden')
    }

    return @($arguments + @('-File', $ScriptPath) + $ScriptArguments)
}
