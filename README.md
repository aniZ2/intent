# Intent

`IntentLayerV01` is now split into a standalone engine plus a NinjaTrader adapter.

## Safety remediation (audit fixes)

A desk-operations audit found execution-safety and quant-validity gaps. These are now fixed (all
changes compile-verified including the strategy, and covered by the build gate `tools\Compile-Check.ps1`):

- **Closed-bar trading (no repaint).** The strategy runs `Calculate.OnBarClose` and decides/enters on
  the last *closed* bar. Previously `OnEachTick` fed the still-forming bar to the engine, so signals
  repainted and live diverged from any bar-close backtest.
- **No naked dashboard orders.** Manual `buy_market`/`sell_market`/`reverse` now always attach a
  protective stop (and target if enabled), bound to the dashboard entry name.
- **Risk controls that actually halt.** The daily loss limit includes open-position P&L, *latches* a
  session kill switch and flattens on breach, applies to manual/dashboard orders too, persists across
  reload, and there is a `MaxContracts` cap. Protective-stop rejection fails safe (flatten + halt).
- **Higher-timeframe filter uses order flow** (`AddVolumetric`) and is evaluated on the closed bar.
- **Authenticated control surface.** `/api/control`, `/api/command`, `/api/strategy-status` require a
  per-session token (written to `%TEMP%\intent-dashboard-token.txt`) plus loopback-Host and
  non-cross-site-Origin checks. The dashboard HTTP/file I/O runs on a background thread, never the
  NinjaTrader instrument thread.
- **Net-P&L backtester.** `Intent.Sweep` now runs an event-driven, cost-aware backtester
  (entry next bar, explicit stop/target, commission + slippage) and ranks configs by post-cost
  expectancy with a pooled out-of-sample number — not cost-free signal F1. New flags:
  `--commission-per-side`, `--slippage-ticks`, `--tick-value`, `--min-trades-for-ranking`. Verify the
  P&L math with `Intent.Sweep.exe --selftest`.
- Capital protections (`UseDailyLossLimit`, `UseFlatBeforeClose`) now default ON.

Bootstrap:
- run `powershell -ExecutionPolicy Bypass -File .\tools\Compile-Check.ps1` (build gate: compiles all
  projects incl. the strategy, runs behavior tests + backtester self-test)
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
- `IntentAutoTraderV01.cs`: full NinjaTrader trading strategy with dashboard bridge, direct manual dashboard order commands, and 5m/15m bias filtering
- `IntentBridgeTestStrategy.cs`: minimal NinjaTrader bridge smoke-test strategy for dashboard-driven demo orders without engine gating
- `IntentLayerV01.Adapter.cs`: explicit NinjaTrader adapter boundary
- `IntentLayerV01.Engine.cs`: NinjaTrader adapter that converts platform state into engine inputs
- `IntentLayerV01.Models.cs`: NinjaTrader-only visual theme models
- `IntentLayerV01.Rendering.cs`: NinjaTrader chart rendering layer

What it detects (5 weighted detectors; composite weights are renormalized at runtime so they need not
sum to exactly 1.0):
- order-flow imbalance
- absorption
- failed breakouts
- liquidity sweeps
- breakout continuation

Outputs:
- `IntentScore` plot: 0-100
- `BullScore` plot
- `BearScore` plot
- on-chart arrow signals
- fixed debug panel
- strategy-driven order entry when `IntentAutoTraderV01.ExecutionMode=Auto`
- manual signal logging when `IntentAutoTraderV01.ExecutionMode=Manual`
- dashboard-driven manual order entry via `Buy Market` / `Sell Market` / `Reverse` / `Flatten`
- local dashboard control/status bridge over `POST /api/control`, `GET /api/command`, and `POST /api/strategy-status` with temp-file fallback
- bridge-only smoke testing via `IntentBridgeTestStrategy`
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
- the dashboard UI is an HTTP control surface; the current long-term target is a dedicated NinjaTrader AddOn control plane, but the repo currently ships a strategy-hosted bridge plus `IntentBridgeTestStrategy` for smoke testing
- standalone compile output was validated locally with `csc.exe` for the engine, console app, and NinjaTrader adapter build
- pure-engine behavior scenarios now include multi-bar `EngineState` sequences, negative/no-trade checks, order-flow precedence checks, and packet/explainability validation via `tools/Validate-Behavior.ps1`
