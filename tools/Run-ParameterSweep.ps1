$ErrorActionPreference = "Stop"

param(
    [Parameter(Mandatory = $true)]
    [string]$InputPath,
    [ValidateSet("combined", "imbalance", "absorption", "weights")]
    [string]$Mode = "combined",
    [string]$OutputPath = "",
    [string]$SignalThresholds = "60",
    [string]$ImbalanceWeights = "0.35",
    [string]$AbsorptionWeights = "0.20",
    [string]$ConfluenceBonuses = "8",
    [string]$ImbalanceRatioThresholds = "2.5",
    [string]$ImbalanceLevelSpans = "4",
    [string]$DeltaPerVolumeBaselines = "0.10",
    [string]$DeltaPerVolumeSpans = "0.40",
    [string]$ImbalanceVolumeSpikeThresholds = "1.15",
    [string]$MinImbalanceVolumePerLevels = "15",
    [string]$AbsorptionDeltaThresholds = "0.22",
    [string]$AbsorptionPriceEfficiencyThresholds = "0.35",
    [string]$AbsorptionWickThresholds = "0.35",
    [string]$AbsorptionWickSpans = "0.65",
    [string]$AbsorptionVolumeSpikeThresholds = "1.20",
    [string]$RangeExpansionPenaltyThresholds = "1.25",
    [string]$NeutralityBuffers = "5",
    [int]$BarSeconds = 60,
    [double]$TickSize = 0.25,
    [int]$TargetTicks = 4,
    [int]$InvalidationTicks = 4,
    [int]$LookaheadBars = 8,
    [int]$TopCount = 3,
    [int]$TrainWindowSessions = 4
)

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$engineDll = Join-Path $repoRoot "src\Intent.Engine\bin\Intent.Engine.dll"
$sweepExe = Join-Path $repoRoot "src\Intent.Sweep\bin\Intent.Sweep.exe"

function Invoke-External {
    param(
        [string]$FilePath,
        [string[]]$Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "External command failed: $FilePath"
    }
}

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $engineDll) | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $sweepExe) | Out-Null

Invoke-External -FilePath "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" -Arguments @(
    "/target:library",
    "/nologo",
    "/out:$engineDll",
    (Join-Path $repoRoot "src\Intent.Engine\Ingestion\OrderFlowPriceLevel.cs"),
    (Join-Path $repoRoot "src\Intent.Engine\Ingestion\IBarBuilder.cs"),
    (Join-Path $repoRoot "src\Intent.Engine\Ingestion\BarBuilder.cs"),
    (Join-Path $repoRoot "src\Intent.Engine\Models\TickData.cs"),
    (Join-Path $repoRoot "src\Intent.Engine\Models\OrderFlowData.cs"),
    (Join-Path $repoRoot "src\Intent.Engine\Models\BarData.cs"),
    (Join-Path $repoRoot "src\Intent.Engine\Models\EngineSettings.cs"),
    (Join-Path $repoRoot "src\Intent.Engine\Transport\TickJsonSerializer.cs"),
    (Join-Path $repoRoot "src\Intent.Engine\State\RollingStatistics.cs"),
    (Join-Path $repoRoot "src\Intent.Engine\State\SessionContext.cs"),
    (Join-Path $repoRoot "src\Intent.Engine\State\EngineState.cs"),
    (Join-Path $repoRoot "src\Intent.Engine\Runtime\StreamDecision.cs"),
    (Join-Path $repoRoot "src\Intent.Engine\Runtime\TickProcessingResult.cs"),
    (Join-Path $repoRoot "src\Intent.Engine\Runtime\IntentRuntime.cs"),
    (Join-Path $repoRoot "src\Intent.Engine\Signals\SignalModels.cs"),
    (Join-Path $repoRoot "src\Intent.Engine\Signals\SignalMath.cs"),
    (Join-Path $repoRoot "src\Intent.Engine\Signals\IntentSignalEngine.cs")
)

Invoke-External -FilePath "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" -Arguments @(
    "/target:exe",
    "/nologo",
    "/out:$sweepExe",
    "/r:$engineDll",
    "/r:System.Runtime.Serialization.dll",
    (Join-Path $repoRoot "src\Intent.Sweep\Program.cs"),
    (Join-Path $repoRoot "src\Intent.Sweep\SweepOptions.cs"),
    (Join-Path $repoRoot "src\Intent.Sweep\TickFileReader.cs"),
    (Join-Path $repoRoot "src\Intent.Sweep\ParameterSweepRunner.cs"),
    (Join-Path $repoRoot "src\Intent.Sweep\SweepSummary.cs")
)

Copy-Item -Force $engineDll (Join-Path (Split-Path -Parent $sweepExe) "Intent.Engine.dll")

$arguments = @(
    "--input", (Resolve-Path $InputPath).Path,
    "--mode", $Mode,
    "--signal-thresholds", $SignalThresholds,
    "--imbalance-weights", $ImbalanceWeights,
    "--absorption-weights", $AbsorptionWeights,
    "--confluence-bonuses", $ConfluenceBonuses,
    "--imbalance-ratio-thresholds", $ImbalanceRatioThresholds,
    "--imbalance-level-spans", $ImbalanceLevelSpans,
    "--delta-per-volume-baselines", $DeltaPerVolumeBaselines,
    "--delta-per-volume-spans", $DeltaPerVolumeSpans,
    "--imbalance-volume-spike-thresholds", $ImbalanceVolumeSpikeThresholds,
    "--min-imbalance-volume-per-levels", $MinImbalanceVolumePerLevels,
    "--absorption-delta-thresholds", $AbsorptionDeltaThresholds,
    "--absorption-price-efficiency-thresholds", $AbsorptionPriceEfficiencyThresholds,
    "--absorption-wick-thresholds", $AbsorptionWickThresholds,
    "--absorption-wick-spans", $AbsorptionWickSpans,
    "--absorption-volume-spike-thresholds", $AbsorptionVolumeSpikeThresholds,
    "--range-expansion-penalty-thresholds", $RangeExpansionPenaltyThresholds,
    "--neutrality-buffers", $NeutralityBuffers,
    "--bar-seconds", $BarSeconds,
    "--tick-size", $TickSize.ToString([System.Globalization.CultureInfo]::InvariantCulture),
    "--target-ticks", $TargetTicks,
    "--invalidation-ticks", $InvalidationTicks,
    "--lookahead-bars", $LookaheadBars,
    "--top-count", $TopCount,
    "--train-window-sessions", $TrainWindowSessions
)

if ($OutputPath) {
    $arguments += @("--output", $OutputPath)
}

Invoke-External -FilePath $sweepExe -Arguments $arguments
