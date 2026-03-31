param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path,
    [switch]$FailOnIssues
)

$ErrorActionPreference = "Stop"

$repoIndexPath = Join-Path $RepoRoot "docs\REPO_INDEX.md"
$apiIndexPath = Join-Path $RepoRoot "docs\NINJATRADER_API_INDEX.md"
$dependencyMapPath = Join-Path $RepoRoot "docs\DEPENDENCY_MAP.md"
$issues = New-Object System.Collections.Generic.List[string]

if (-not (Test-Path $repoIndexPath)) {
    $issues.Add("Missing generated repo index: docs/REPO_INDEX.md")
} else {
    $repoLines = Get-Content $repoIndexPath
    $indexedPaths = @{}
    $inFilesSection = $false

    foreach ($line in $repoLines) {
        if ($line -eq "## Files") {
            $inFilesSection = $true
            continue
        }

        if ($line -like "## *" -and $line -ne "## Files") {
            $inFilesSection = $false
        }

        if ($inFilesSection -and $line -match '^- ([^:]+): (.+)$') {
            $indexedPaths[$matches[1].Trim()] = $matches[2].Trim()
        }
    }

    $repoFiles = Get-ChildItem -Path $RepoRoot -Recurse -File -Include *.cs,*.csproj,*.md,*.ps1 |
        Where-Object { $_.FullName -notmatch "\\bin\\|\\obj\\|\\.git\\" }

    foreach ($file in $repoFiles) {
        $relativePath = $file.FullName.Substring($RepoRoot.Length).TrimStart('\')

        if (-not $indexedPaths.ContainsKey($relativePath)) {
            $issues.Add("Unindexed file: $relativePath")
            continue
        }

        if ($indexedPaths[$relativePath] -eq "Project file.") {
            $issues.Add("Generic summary still present for: $relativePath")
        }
    }

    foreach ($indexedPath in $indexedPaths.Keys) {
        if (-not (Test-Path (Join-Path $RepoRoot $indexedPath))) {
            $issues.Add("Stale repo index entry: $indexedPath")
        }
    }
}

if (-not (Test-Path $apiIndexPath)) {
    $issues.Add("Missing generated API index: docs/NINJATRADER_API_INDEX.md")
}

if (-not (Test-Path $dependencyMapPath)) {
    $issues.Add("Missing generated dependency map: docs/DEPENDENCY_MAP.md")
}

if ($issues.Count -eq 0) {
    Write-Output "Gap analysis: no issues found."
    exit 0
}

Write-Output "Gap analysis found issues:"
foreach ($issue in $issues) {
    Write-Output ("- {0}" -f $issue)
}

if ($FailOnIssues) {
    exit 1
}
