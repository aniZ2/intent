param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path,
    [switch]$FailOnIssues
)

$ErrorActionPreference = "Stop"
$issues = New-Object System.Collections.Generic.List[string]

function Test-ForbiddenPattern {
    param(
        [string]$Path,
        [string[]]$Patterns,
        [string]$Message
    )

    foreach ($pattern in $Patterns) {
        $match = Select-String -Path $Path -Pattern $pattern -SimpleMatch
        if ($match) {
            $issues.Add("$Message -> $Path")
            break
        }
    }
}

$engineFiles = Get-ChildItem (Join-Path $RepoRoot "src\Intent.Engine") -Recurse -File -Include *.cs
foreach ($file in $engineFiles) {
    Test-ForbiddenPattern -Path $file.FullName -Patterns @("NinjaTrader.", "System.Windows.", "PresentationCore", "PresentationFramework") -Message "Pure engine contains platform/UI reference"
}

$consoleProject = Join-Path $RepoRoot "src\Intent.Console\Intent.Console.csproj"
if (Test-Path $consoleProject) {
    $consoleProjectText = Get-Content $consoleProject -Raw
    if ($consoleProjectText -match "NinjaTrader8\\IntentLayerV01\.csproj" -or $consoleProjectText -match "NinjaTrader") {
        $issues.Add("Console project must not reference NinjaTrader assemblies or projects")
    }
}

$ninjaProject = Join-Path $RepoRoot "src\NinjaTrader8\IntentLayerV01.csproj"
if (Test-Path $ninjaProject) {
    $ninjaProjectText = Get-Content $ninjaProject -Raw
    if ($ninjaProjectText -notmatch "Intent\.Engine\\Intent\.Engine\.csproj") {
        $issues.Add("NinjaTrader project must reference Intent.Engine")
    }
}

$adapterContract = Join-Path $RepoRoot "src\NinjaTrader8\Indicators\IntentLayerV01.Adapter.cs"
if (-not (Test-Path $adapterContract)) {
    $issues.Add("Missing explicit NinjaTrader adapter contract")
}

$renderingFile = Join-Path $RepoRoot "src\NinjaTrader8\Indicators\IntentLayerV01.Rendering.cs"
if (Test-Path $renderingFile) {
    $renderingText = Get-Content $renderingFile -Raw
    if ($renderingText -match "VolumetricBarsType" -or $renderingText -match "GetAskVolumeForPrice" -or $renderingText -match "GetBidVolumeForPrice") {
        $issues.Add("Rendering layer should not directly inspect NinjaTrader volumetric APIs")
    }
}

$adapterFile = Join-Path $RepoRoot "src\NinjaTrader8\Indicators\IntentLayerV01.Engine.cs"
if (Test-Path $adapterFile) {
    $adapterText = Get-Content $adapterFile -Raw
    if ($adapterText -match "class IntentSignalEngine") {
        $issues.Add("Adapter file should not re-implement standalone signal engine logic")
    }
}

if ($issues.Count -eq 0) {
    Write-Output "Architecture validation: passed."
    exit 0
}

Write-Output "Architecture validation found issues:"
foreach ($issue in $issues) {
    Write-Output ("- {0}" -f $issue)
}

if ($FailOnIssues) {
    exit 1
}
