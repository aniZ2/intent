param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path,
    [string]$OutputPath = ""
)

$ErrorActionPreference = "Stop"

if (-not $OutputPath) {
    $OutputPath = Join-Path $RepoRoot "docs\DEPENDENCY_MAP.md"
}

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("# Dependency Map")
$lines.Add("")
$lines.Add(("Generated: {0:yyyy-MM-dd HH:mm:ss zzz}" -f (Get-Date)))
$lines.Add("")

$projectFiles = Get-ChildItem (Join-Path $RepoRoot "src") -Recurse -File -Filter *.csproj | Sort-Object FullName
foreach ($project in $projectFiles) {
    $relativeProject = $project.FullName.Substring($RepoRoot.Length).TrimStart('\')
    $projectText = Get-Content $project.FullName -Raw
    $lines.Add(("## {0}" -f $relativeProject))
    $lines.Add("")
    $lines.Add("### Project References")
    $lines.Add("")

    $projectRefMatches = [regex]::Matches($projectText, 'ProjectReference Include="([^"]+)"')
    if ($projectRefMatches.Count -eq 0) {
        $lines.Add("- none")
    } else {
        foreach ($match in $projectRefMatches) {
            $lines.Add(('- `{0}`' -f $match.Groups[1].Value))
        }
    }

    $lines.Add("")
}

$directory = Split-Path -Parent $OutputPath
if ($directory) {
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
}

Set-Content -Path $OutputPath -Value $lines -Encoding UTF8
Write-Output "Wrote $OutputPath"
