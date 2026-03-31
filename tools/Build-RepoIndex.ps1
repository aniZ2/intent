param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path,
    [string]$OutputPath = ""
)

$ErrorActionPreference = "Stop"

if (-not $OutputPath) {
    $OutputPath = Join-Path $RepoRoot "docs\REPO_INDEX.md"
}

$sourceFiles = Get-ChildItem -Path $RepoRoot -Recurse -File -Include *.cs,*.csproj,*.md,*.ps1 |
    Where-Object { $_.FullName -notmatch "\\bin\\|\\obj\\|\\.git\\" } |
    Sort-Object FullName

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("# Repo Index")
$lines.Add("")
$lines.Add(("Generated: {0:yyyy-MM-dd HH:mm:ss zzz}" -f (Get-Date)))
$lines.Add("")
$lines.Add("## Files")
$lines.Add("")

foreach ($file in $sourceFiles) {
    $relativePath = $file.FullName.Substring($RepoRoot.Length).TrimStart('\')
    $summary = ""

    switch -Wildcard ($relativePath) {
        "src\Intent.Console\Intent.Console.csproj" { $summary = "Project file for the standalone TCP stream runner."; break }
        "src\Intent.Console\DecisionPacketSink.cs" { $summary = "Append-only NDJSON sink for emitted decision packets."; break }
        "src\Intent.Console\DashboardBroadcaster.cs" { $summary = "Optional local HTTP dashboard that streams live decision packets."; break }
        "src\Intent.Console\Program.cs" { $summary = "Process entry point for the TCP tick listener host."; break }
        "src\Intent.Console\RawTickArchive.cs" { $summary = "Archives inbound raw ticks into session NDJSON files for replay and sweeps."; break }
        "src\Intent.Console\RunnerOptions.cs" { $summary = "Command-line options for the standalone stream runner."; break }
        "src\Intent.Console\RunnerLogger.cs" { $summary = "Timestamped console/file logger for stream runner events."; break }
        "src\Intent.Console\RuntimeFactory.cs" { $summary = "Builds the runtime pipeline from runner options."; break }
        "src\Intent.Console\TcpTickServer.cs" { $summary = "TCP listener that reads line-delimited tick JSON and emits decision packets."; break }
        "src\Intent.Console\TickJsonDeserializer.cs" { $summary = "Safe JSON parser for inbound tick payloads."; break }
        "src\Intent.Replay\Intent.Replay.csproj" { $summary = "Project file for deterministic tick replay."; break }
        "src\Intent.Replay\Program.cs" { $summary = "Entry point for replaying recorded tick streams."; break }
        "src\Intent.Replay\ReplayOptions.cs" { $summary = "Command-line options for tick replay sessions."; break }
        "src\Intent.Replay\TickReplayClient.cs" { $summary = "TCP client that replays recorded tick JSON with optional pacing."; break }
        "src\Intent.Sweep\Intent.Sweep.csproj" { $summary = "Project file for offline parameter sweeps against replay ticks."; break }
        "src\Intent.Sweep\Program.cs" { $summary = "Entry point for running parameter sweeps over replay data."; break }
        "src\Intent.Sweep\SweepOptions.cs" { $summary = "Command-line options and sweep axes for offline tuning."; break }
        "src\Intent.Sweep\TickFileReader.cs" { $summary = "Reads NDJSON tick captures into platform-neutral tick models."; break }
        "src\Intent.Sweep\ParameterSweepRunner.cs" { $summary = "Executes combinations of engine settings and summarizes outcomes."; break }
        "src\Intent.Sweep\SweepSummary.cs" { $summary = "JSON-ready sweep summary model with score and latency metrics."; break }
        "src\Intent.Engine\Intent.Engine.csproj" { $summary = "Pure engine project for models, ingestion, state, and signal logic."; break }
        "src\Intent.Engine.Tests\Intent.Engine.Tests.csproj" { $summary = "Standalone behavior-test project for pure engine scenarios."; break }
        "src\Intent.Engine.Tests\Program.cs" { $summary = "Scenario-based behavioral tests for the pure signal engine."; break }
        "src\Intent.Engine\Models\TickData.cs" { $summary = "Pure tick model for standalone ingestion."; break }
        "src\Intent.Engine\Models\OrderFlowData.cs" { $summary = "Platform-neutral order-flow snapshot model."; break }
        "src\Intent.Engine\Models\BarData.cs" { $summary = "Platform-neutral completed bar model consumed by the engine."; break }
        "src\Intent.Engine\Models\EngineSettings.cs" { $summary = "Standalone engine configuration."; break }
        "src\Intent.Engine\Transport\TickJsonSerializer.cs" { $summary = "Shared tick-to-JSON serializer for streaming."; break }
        "src\Intent.Engine\Signals\IntentSignalEngine.cs" { $summary = "Pure signal detection and scoring logic."; break }
        "src\Intent.Engine\Signals\SignalMath.cs" { $summary = "Pure engine math helpers."; break }
        "src\Intent.Engine\Signals\SignalModels.cs" { $summary = "Signal result and direction models."; break }
        "src\Intent.Engine\State\RollingStatistics.cs" { $summary = "Reusable rolling averages for engine state."; break }
        "src\Intent.Engine\State\SessionContext.cs" { $summary = "Session-level running context."; break }
        "src\Intent.Engine\State\EngineState.cs" { $summary = "Standalone engine state across completed bars."; break }
        "src\Intent.Engine\Runtime\IntentRuntime.cs" { $summary = "Hybrid tick-processing runtime that emits completed-bar and signal packets."; break }
        "src\Intent.Engine\Runtime\StreamDecision.cs" { $summary = "Emission model for runtime decision events."; break }
        "src\Intent.Engine\Runtime\TickProcessingResult.cs" { $summary = "Per-tick processing result with completed bars and emitted packets."; break }
        "src\Intent.Engine\Ingestion\OrderFlowPriceLevel.cs" { $summary = "Price-level bid/ask volume model."; break }
        "src\Intent.Engine\Ingestion\IBarBuilder.cs" { $summary = "Interface for building bars from lower-level inputs."; break }
        "src\Intent.Engine\Ingestion\BarBuilder.cs" { $summary = "Pure tick-to-bar ingestion pipeline."; break }
        "src\NinjaTrader8\Indicators\IntentLayerV01.Adapter.cs" { $summary = "Adapter contract between NinjaTrader and the standalone engine."; break }
        "src\NinjaTrader8\Indicators\IntentLayerV01.cs" { $summary = "Indicator orchestration, parameters, lifecycle, plots."; break }
        "src\NinjaTrader8\Indicators\IntentLayerV01.Engine.cs" { $summary = "NinjaTrader adapter implementation that converts platform data into engine inputs."; break }
        "src\NinjaTrader8\Indicators\IntentLayerV01.Models.cs" { $summary = "NinjaTrader-only visual theme models."; break }
        "src\NinjaTrader8\Indicators\IntentLayerV01.Rendering.cs" { $summary = "Chart drawings, marker rendering, debug panel output."; break }
        "src\NinjaTrader8\Indicators\IntentLayerV01.Streaming.cs" { $summary = "NinjaTrader TCP tick publisher for the standalone stream runner."; break }
        "src\NinjaTrader8\IntentLayerV01.csproj" { $summary = "Build configuration and NinjaTrader assembly references."; break }
        "tools\Analyze-Gaps.ps1" { $summary = "Detects stale, missing, or weak index coverage."; break }
        "tools\Build-DependencyMap.ps1" { $summary = "Generates the current project dependency map."; break }
        "tools\Build-RepoIndex.ps1" { $summary = "Generates this repo index."; break }
        "tools\Build-NinjaTraderApiIndex.ps1" { $summary = "Reflects local NinjaTrader assemblies into an API snapshot."; break }
        "tools\Check-GeneratedArtifacts.ps1" { $summary = "Verifies generated docs are current."; break }
        "tools\Diff-NinjaTraderApiIndex.ps1" { $summary = "Diffs the current API snapshot against a baseline."; break }
        "tools\Refresh-Indexes.ps1" { $summary = "Runs all repo bootstrap index generators."; break }
        "tools\Run-ParameterSweep.ps1" { $summary = "Compiles and runs offline parameter sweeps over NDJSON ticks."; break }
        "tools\Run-Verification.ps1" { $summary = "Runs the full maintenance and verification pipeline."; break }
        "tools\Validate-Behavior.ps1" { $summary = "Runs scenario-based pure engine behavior tests."; break }
        "tools\Validate-Architecture.ps1" { $summary = "Validates engine, adapter, and rendering separation rules."; break }
        "docs\BOOTSTRAP.md" { $summary = "Bootstrap instructions for indexing and local API discovery."; break }
        "docs\DEPENDENCY_MAP.md" { $summary = "Generated project-level dependency map."; break }
        "docs\FULL_SYSTEM_REFERENCE.md" { $summary = "Consolidated system reference for architecture, runtime, and tooling."; break }
        "docs\LIVE_BRINGUP.md" { $summary = "Step-by-step guide for connecting NinjaTrader, the stream runner, and local archives."; break }
        "docs\NINJATRADER_API_INDEX.md" { $summary = "Generated snapshot of local NinjaTrader APIs used by the project."; break }
        "docs\REPO_INDEX.md" { $summary = "Generated file map for the repo."; break }
        "docs\STREAMING_CONTRACT.md" { $summary = "Wire contract for tick ingress and decision egress."; break }
        "README.md" { $summary = "Project overview and usage notes."; break }
        default { $summary = "Project file."; break }
    }

    $lines.Add(("- `{0}`: {1}" -f $relativePath, $summary))
}

$lines.Add("")
$lines.Add("## Structure")
$lines.Add("")
$lines.Add('- `src/Intent.Engine`: pure runtime-independent models, ingestion, state, and signals.')
$lines.Add('- `src/Intent.Engine/Transport`: shared streaming serializers for wire payloads.')
$lines.Add('- `src/Intent.Engine.Tests`: scenario-driven pure engine behavior validation.')
$lines.Add('- `src/Intent.Console`: standalone TCP stream runner and ingestion host.')
$lines.Add('- `src/Intent.Replay`: deterministic TCP replay client for streaming tests.')
$lines.Add('- `src/Intent.Sweep`: offline parameter sweep worker for settings experiments.')
$lines.Add('- `src/NinjaTrader8/Indicators`: runtime indicator code split by concern.')
$lines.Add('- `tools`: repeatable helper scripts for indexing and local API discovery.')
$lines.Add('- `docs`: generated reference outputs used to bootstrap future work.')

$directory = Split-Path -Parent $OutputPath
if ($directory) {
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
}

Set-Content -Path $OutputPath -Value $lines -Encoding UTF8
Write-Output "Wrote $OutputPath"
