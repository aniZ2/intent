# Intent

`IntentLayerV01` is now split into a standalone engine plus a NinjaTrader adapter.

Bootstrap:
- run `powershell -ExecutionPolicy Bypass -File .\tools\Refresh-Indexes.ps1`
- run `powershell -ExecutionPolicy Bypass -File .\tools\Validate-Behavior.ps1`
- run `powershell -ExecutionPolicy Bypass -File .\tools\Run-Verification.ps1`
- read `docs/REPO_INDEX.md`
- read `docs/NINJATRADER_API_INDEX.md`
- read `docs/DEPENDENCY_MAP.md`
- read `docs/STREAMING_CONTRACT.md`
- read `docs/LIVE_BRINGUP.md`

Files:
- `src/Intent.Engine`: pure C# engine, models, scoring, signal detection
- `src/Intent.Engine.Tests`: scenario-based pure engine behavior tests
- `src/Intent.Engine/Ingestion`: tick-to-bar builder and price-level order-flow models
- `src/Intent.Engine/State`: rolling/session state used by the standalone engine
- `src/Intent.Console`: `Intent.StreamRunner`, a standalone TCP stream runner for the engine
- `src/Intent.Replay`: deterministic tick replay client for end-to-end streaming tests
- `src/Intent.Sweep`: offline parameter sweep worker for replay-driven settings experiments
- `docs/STREAMING_CONTRACT.md`: line-delimited JSON contract for inbound ticks and outbound decisions
- `IntentLayerV01.cs`: public indicator, properties, plots, chart drawings, debug panel
- `IntentLayerV01.Adapter.cs`: explicit NinjaTrader adapter boundary
- `IntentLayerV01.Engine.cs`: NinjaTrader adapter that converts platform state into engine inputs
- `IntentLayerV01.Models.cs`: NinjaTrader-only visual theme models
- `IntentLayerV01.Rendering.cs`: NinjaTrader chart rendering layer

What it detects:
- order-flow imbalance
- absorption
- failed breakouts
- liquidity sweeps

Outputs:
- `IntentScore` plot: 0-100
- `BullScore` plot
- `BearScore` plot
- on-chart arrow signals
- fixed debug panel
- structured decision packets from the pure engine (`SignalResult.ToDecisionPacket()` / JSON-ready `ToJson()`)
- line-delimited TCP tick ingestion with hybrid emission (`barClose` + thresholded `signal` packets)
- NinjaTrader can now emit live ticks over TCP when `EnableTickStreaming=true`
- stream runner supports env/CLI config and optional file logging for live sessions
- stream runner can persist emitted decision packets to append-only NDJSON for later replay or analytics
- stream runner can archive raw inbound ticks into session NDJSON files for later sweep runs
- stream runner can expose a local live dashboard over HTTP for packet inspection
- decision packets now include `latencyMs` and `dataQuality` for live/replay diagnostics

Offline tuning:
- run `powershell -ExecutionPolicy Bypass -File .\tools\Run-ParameterSweep.ps1 -InputPath .\ticks.ndjson -SignalThresholds 55,60,65 -ImbalanceWeights 0.25,0.35 -AbsorptionDeltaThresholds 0.18,0.22 -NeutralityBuffers 4,5`

Notes:
- this version is compile-safe and now uses NinjaTrader Order Flow+ volumetric bid/ask data for imbalance, absorption, and order-flow confirmation
- use it on volumetric bars; the debug panel will show `N/A` when volumetric data is unavailable
- standalone compile output was validated locally with `csc.exe` for the engine, console app, and NinjaTrader adapter build
- pure-engine behavior scenarios now include multi-bar `EngineState` sequences, negative/no-trade checks, order-flow precedence checks, and packet/explainability validation via `tools/Validate-Behavior.ps1`
