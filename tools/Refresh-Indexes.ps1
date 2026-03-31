$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

& (Join-Path $PSScriptRoot "Build-NinjaTraderApiIndex.ps1") -OutputPath (Join-Path $repoRoot "docs\NINJATRADER_API_INDEX.md")
& (Join-Path $PSScriptRoot "Build-DependencyMap.ps1") -RepoRoot $repoRoot | Out-Null
& (Join-Path $PSScriptRoot "Build-RepoIndex.ps1") -RepoRoot $repoRoot

Write-Output "Refreshed repo indexes."
