# Repo Index

Generated: 2026-03-30 23:23:45 -04:00

## Files

- docs\BOOTSTRAP.md: Bootstrap instructions for indexing and local API discovery.
- docs\DEPENDENCY_MAP.md: Generated project-level dependency map.
- docs\FULL_SYSTEM_REFERENCE.md: Consolidated system reference for architecture, runtime, and tooling.
- docs\LIVE_BRINGUP.md: Step-by-step guide for connecting NinjaTrader, the stream runner, and local archives.
- docs\NINJATRADER_API_INDEX.md: Generated snapshot of local NinjaTrader APIs used by the project.
- docs\REPO_INDEX.md: Generated file map for the repo.
- docs\STREAMING_CONTRACT.md: Wire contract for tick ingress and decision egress.
- README.md: Project overview and usage notes.
- src\Intent.Console\DashboardBroadcaster.cs: Optional local HTTP dashboard that streams live decision packets.
- src\Intent.Console\DecisionPacketSink.cs: Append-only NDJSON sink for emitted decision packets.
- src\Intent.Console\Intent.Console.csproj: Project file for the standalone TCP stream runner.
- src\Intent.Console\Program.cs: Process entry point for the TCP tick listener host.
- src\Intent.Console\RawTickArchive.cs: Archives inbound raw ticks into session NDJSON files for replay and sweeps.
- src\Intent.Console\RunnerLogger.cs: Timestamped console/file logger for stream runner events.
- src\Intent.Console\RunnerOptions.cs: Command-line options for the standalone stream runner.
- src\Intent.Console\RuntimeFactory.cs: Builds the runtime pipeline from runner options.
- src\Intent.Console\TcpTickServer.cs: TCP listener that reads line-delimited tick JSON and emits decision packets.
- src\Intent.Console\TickJsonDeserializer.cs: Safe JSON parser for inbound tick payloads.
- src\Intent.Engine.Tests\Intent.Engine.Tests.csproj: Standalone behavior-test project for pure engine scenarios.
- src\Intent.Engine.Tests\Program.cs: Scenario-based behavioral tests for the pure signal engine.
- src\Intent.Engine\Ingestion\BarBuilder.cs: Pure tick-to-bar ingestion pipeline.
- src\Intent.Engine\Ingestion\IBarBuilder.cs: Interface for building bars from lower-level inputs.
- src\Intent.Engine\Ingestion\OrderFlowPriceLevel.cs: Price-level bid/ask volume model.
- src\Intent.Engine\Intent.Engine.csproj: Pure engine project for models, ingestion, state, and signal logic.
- src\Intent.Engine\Models\BarData.cs: Platform-neutral completed bar model consumed by the engine.
- src\Intent.Engine\Models\EngineSettings.cs: Standalone engine configuration.
- src\Intent.Engine\Models\OrderFlowData.cs: Platform-neutral order-flow snapshot model.
- src\Intent.Engine\Models\TickData.cs: Pure tick model for standalone ingestion.
- src\Intent.Engine\Runtime\IntentRuntime.cs: Hybrid tick-processing runtime that emits completed-bar and signal packets.
- src\Intent.Engine\Runtime\StreamDecision.cs: Emission model for runtime decision events.
- src\Intent.Engine\Runtime\TickProcessingResult.cs: Per-tick processing result with completed bars and emitted packets.
- src\Intent.Engine\Signals\IntentSignalEngine.cs: Pure signal detection and scoring logic.
- src\Intent.Engine\Signals\SignalMath.cs: Pure engine math helpers.
- src\Intent.Engine\Signals\SignalModels.cs: Signal result and direction models.
- src\Intent.Engine\State\EngineState.cs: Standalone engine state across completed bars.
- src\Intent.Engine\State\RollingStatistics.cs: Reusable rolling averages for engine state.
- src\Intent.Engine\State\SessionContext.cs: Session-level running context.
- src\Intent.Engine\Transport\TickJsonSerializer.cs: Shared tick-to-JSON serializer for streaming.
- src\Intent.Replay\Intent.Replay.csproj: Project file for deterministic tick replay.
- src\Intent.Replay\Program.cs: Entry point for replaying recorded tick streams.
- src\Intent.Replay\ReplayOptions.cs: Command-line options for tick replay sessions.
- src\Intent.Replay\TickReplayClient.cs: TCP client that replays recorded tick JSON with optional pacing.
- src\Intent.Sweep\Intent.Sweep.csproj: Project file for offline parameter sweeps against replay ticks.
- src\Intent.Sweep\ParameterSweepRunner.cs: Executes combinations of engine settings and summarizes outcomes.
- src\Intent.Sweep\Program.cs: Entry point for running parameter sweeps over replay data.
- src\Intent.Sweep\SweepOptions.cs: Command-line options and sweep axes for offline tuning.
- src\Intent.Sweep\SweepSummary.cs: JSON-ready sweep summary model with score and latency metrics.
- src\Intent.Sweep\TickFileReader.cs: Reads NDJSON tick captures into platform-neutral tick models.
- src\NinjaTrader8\Indicators\IntentLayerV01.Adapter.cs: Adapter contract between NinjaTrader and the standalone engine.
- src\NinjaTrader8\Indicators\IntentLayerV01.cs: Indicator orchestration, parameters, lifecycle, plots.
- src\NinjaTrader8\Indicators\IntentLayerV01.Engine.cs: NinjaTrader adapter implementation that converts platform data into engine inputs.
- src\NinjaTrader8\Indicators\IntentLayerV01.Models.cs: NinjaTrader-only visual theme models.
- src\NinjaTrader8\Indicators\IntentLayerV01.Rendering.cs: Chart drawings, marker rendering, debug panel output.
- src\NinjaTrader8\Indicators\IntentLayerV01.Streaming.cs: NinjaTrader TCP tick publisher for the standalone stream runner.
- src\NinjaTrader8\IntentLayerV01.csproj: Build configuration and NinjaTrader assembly references.
- tools\Analyze-Gaps.ps1: Detects stale, missing, or weak index coverage.
- tools\Build-DependencyMap.ps1: Generates the current project dependency map.
- tools\Build-NinjaTraderApiIndex.ps1: Reflects local NinjaTrader assemblies into an API snapshot.
- tools\Build-RepoIndex.ps1: Generates this repo index.
- tools\Check-GeneratedArtifacts.ps1: Verifies generated docs are current.
- tools\Diff-NinjaTraderApiIndex.ps1: Diffs the current API snapshot against a baseline.
- tools\Refresh-Indexes.ps1: Runs all repo bootstrap index generators.
- tools\Run-ParameterSweep.ps1: Compiles and runs offline parameter sweeps over NDJSON ticks.
- tools\Run-Verification.ps1: Runs the full maintenance and verification pipeline.
- tools\Validate-Architecture.ps1: Validates engine, adapter, and rendering separation rules.
- tools\Validate-Behavior.ps1: Runs scenario-based pure engine behavior tests.

## Structure

- `src/Intent.Engine`: pure runtime-independent models, ingestion, state, and signals.
- `src/Intent.Engine/Transport`: shared streaming serializers for wire payloads.
- `src/Intent.Engine.Tests`: scenario-driven pure engine behavior validation.
- `src/Intent.Console`: standalone TCP stream runner and ingestion host.
- `src/Intent.Replay`: deterministic TCP replay client for streaming tests.
- `src/Intent.Sweep`: offline parameter sweep worker for settings experiments.
- `src/NinjaTrader8/Indicators`: runtime indicator code split by concern.
- `tools`: repeatable helper scripts for indexing and local API discovery.
- `docs`: generated reference outputs used to bootstrap future work.
