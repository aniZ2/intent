param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
)

$ErrorActionPreference = "Stop"

$tempDir = Join-Path ([System.IO.Path]::GetTempPath()) ("intent-doc-check-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $tempDir | Out-Null

$tempRepoIndex = Join-Path $tempDir "REPO_INDEX.md"
$tempApiIndex = Join-Path $tempDir "NINJATRADER_API_INDEX.md"
$tempDependencyMap = Join-Path $tempDir "DEPENDENCY_MAP.md"

& (Join-Path $PSScriptRoot "Build-NinjaTraderApiIndex.ps1") -OutputPath $tempApiIndex | Out-Null
& (Join-Path $PSScriptRoot "Build-DependencyMap.ps1") -RepoRoot $RepoRoot -OutputPath $tempDependencyMap | Out-Null
& (Join-Path $PSScriptRoot "Build-RepoIndex.ps1") -RepoRoot $RepoRoot -OutputPath $tempRepoIndex | Out-Null

$checks = @(
    @{ Actual = (Join-Path $RepoRoot "docs\REPO_INDEX.md"); Generated = $tempRepoIndex; Name = "docs/REPO_INDEX.md" },
    @{ Actual = (Join-Path $RepoRoot "docs\NINJATRADER_API_INDEX.md"); Generated = $tempApiIndex; Name = "docs/NINJATRADER_API_INDEX.md" },
    @{ Actual = (Join-Path $RepoRoot "docs\DEPENDENCY_MAP.md"); Generated = $tempDependencyMap; Name = "docs/DEPENDENCY_MAP.md" }
)

$hasDrift = $false

function Get-NormalizedGeneratedContent {
    param([string]$Path)

    $lines = Get-Content $Path |
        Where-Object { $_ -notmatch '^Generated: ' } |
        ForEach-Object { $_.TrimEnd() }

    while ($lines.Count -gt 0 -and [string]::IsNullOrWhiteSpace($lines[$lines.Count - 1])) {
        if ($lines.Count -eq 1) {
            $lines = @()
        } else {
            $lines = $lines[0..($lines.Count - 2)]
        }
    }

    return ($lines -join "`n")
}

foreach ($check in $checks) {
    if (-not (Test-Path $check.Actual) -or (Get-NormalizedGeneratedContent $check.Actual) -ne (Get-NormalizedGeneratedContent $check.Generated)) {
        Write-Output ("Generated artifact drift detected: {0}" -f $check.Name)
        $hasDrift = $true
    }
}

Remove-Item -Recurse -Force $tempDir

if ($hasDrift) {
    exit 1
}

Write-Output "Generated artifacts are current."
