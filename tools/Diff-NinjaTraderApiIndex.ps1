param(
    [string]$BaselinePath = "",
    [string]$CurrentPath = ""
)

$ErrorActionPreference = "Stop"

if (-not $CurrentPath) {
    $repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
    $CurrentPath = Join-Path $repoRoot "docs\NINJATRADER_API_INDEX.md"
}

if (-not $BaselinePath) {
    throw "Provide -BaselinePath to compare against a previous API snapshot."
}

if (-not (Test-Path $BaselinePath)) {
    throw "Baseline path not found: $BaselinePath"
}

if (-not (Test-Path $CurrentPath)) {
    throw "Current path not found: $CurrentPath"
}

$baseline = Get-Content $BaselinePath
$current = Get-Content $CurrentPath

Compare-Object -ReferenceObject $baseline -DifferenceObject $current |
    ForEach-Object {
        $prefix = if ($_.SideIndicator -eq "=>") { "+" } else { "-" }
        Write-Output ("{0} {1}" -f $prefix, $_.InputObject)
    }
