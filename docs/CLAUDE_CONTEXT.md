# Intent — Complete System Context

You are working on **Intent**, a real-time trading signal detection engine written in C# (.NET Framework 4.8). This document is the authoritative reference for the entire system. Everything described here reflects the actual code as of 2026-03-31.

---

## What Intent Does

Intent analyzes price action and order flow (bid/ask volume at each price level) to detect five market microstructure patterns in real time:

1. **Order Flow Imbalance** — stacked aggressive volume on one side across multiple price levels with directional delta confirmation
2. **Absorption** — one side's aggression absorbed by the other; heavy volume but price doesn't follow the aggressor (shows as a wick/tail)
3. **Failed Breakout** — price breaks beyond a prior swing level then reverses and closes back inside (a trap)
4. **Liquidity Sweep** — price sweeps through a prior swing into a low-volume zone then snaps back with a rejection wick
5. **Breakout Continuation** — price breaks through a prior swing level and holds, confirming directional momentum

Each detector independently scores bullish (0-100) and bearish (0-100). The composite IntentScore = max(weighted bullish, weighted bearish). Signals emit when IntentScore >= threshold (default 60).

Every signal includes full explainability: per-factor breakdowns (name, raw value, normalized value, weight, contribution), reasons, confidence levels, invalidation prices, and target zones.

---

## How It Runs

Intent runs two ways from the same pure engine:

**1. Inside NinjaTrader 8** — as a chart indicator (`IntentLayerV01`) consuming live volumetric bar data from Order Flow+, as a full automated/manual trading strategy (`IntentAutoTraderV01`), and as a minimal bridge smoke-test strategy (`IntentBridgeTestStrategy`) for dashboard-driven demo order verification.

**2. Standalone** — as a TCP server (`Intent.Console`) consuming line-delimited JSON ticks from any source. A replay client (`Intent.Replay`) can feed recorded tick files. A parameter sweep tool (`Intent.Sweep`) optimizes detection parameters via walk-forward validation.

---

## Architecture

```
┌──────────────────────────────────────────────────────────┐
│                  Intent.Engine (pure C#, no deps)         │
│                                                          │
│  Models:     TickData, BarData, OrderFlowData,           │
│              EngineSettings, OrderFlowPriceLevel          │
│                                                          │
│  Ingestion:  BarBuilder (tick-to-bar + order flow)       │
│                                                          │
│  State:      EngineState, RollingStatistics,             │
│              SessionContext                               │
│                                                          │
│  Signals:    IntentSignalEngine (5 detectors + scoring)  │
│              SignalMath, SignalModels                     │
│                                                          │
│  Runtime:    IntentRuntime (tick processor + emitter)     │
│                                                          │
│  Transport:  TickJsonSerializer                          │
└────────┬─────────────┬──────────────┬────────────────────┘
         │             │              │
┌────────▼───────┐ ┌───▼────────┐ ┌──▼───────────────────┐
│ Intent.Console │ │ Intent.    │ │ NinjaTrader8         │
│                │ │ Sweep      │ │                      │
│ TcpTickServer  │ │            │ │ IntentLayerV01       │
│ TickJsonDeser. │ │ Parameter  │ │  (indicator, 6 files)│
│ DecisionPacket │ │ SweepRunner│ │                      │
│   Sink         │ │ TickFile   │ │ IntentAutoTraderV01  │
│ RawTickArchive │ │   Reader   │ │  (strategy)          │
│ DashboardBroad.│ │ SweepOpts  │ │                      │
│ RunnerLogger   │ │ SweepSumm. │ └──────────────────────┘
│ RuntimeFactory │ └────────────┘
│ RunnerOptions  │
└────────────────┘

┌────────────────┐  ┌──────────────────┐
│ Intent.Replay  │  │ Intent.Engine    │
│                │  │   .Tests         │
│ TickReplayClnt │  │                  │
│ ReplayOptions  │  │ 17 behavioral    │
│                │  │ test scenarios   │
└────────────────┘  └──────────────────┘
```

**Dependency graph (one-directional, no cycles):**

- `Intent.Engine` → nothing (pure library)
- `Intent.Console` → `Intent.Engine`
- `Intent.Sweep` → `Intent.Engine`
- `Intent.Replay` → nothing (standalone TCP client)
- `Intent.Engine.Tests` → `Intent.Engine`
- `NinjaTrader8/IntentLayerV01` → `Intent.Engine` + NinjaTrader platform APIs
- `NinjaTrader8/IntentAutoTraderV01` → `Intent.Engine` + NinjaTrader platform APIs

All projects target .NET Framework 4.8. All compile standalone via `csc.exe` (no MSBuild/Visual Studio required).

---

## Every File and Its Purpose

### Intent.Engine (src/Intent.Engine/) — 17 files

| File | Purpose |
|------|---------|
| `Models/TickData.cs` | Single trade: TimestampUtc, Instrument, Price, Volume, Bid, Ask, IsBuyerInitiated |
| `Models/BarData.cs` | Completed bar: OHLCV + 22 computed properties (Range, Body, BodyRatio, UpperWick, LowerWick, UpperWickRatio, LowerWickRatio, CloseLocation, VolumeSpike, RangeExpansion, BreakAboveDistance, BreakBelowDistance, ReclaimBelowHigh, ReclaimAboveLow, BreakAboveTicks, BreakBelowTicks, ReclaimBelowHighTicks, ReclaimAboveLowTicks, PriceEfficiency, IsBullishBody, IsBearishBody) + PriorSignalDirection, PriorIntentScore, OrderFlow |
| `Models/OrderFlowData.cs` | Volumetric snapshot: IsAvailable, TotalBuyingVolume, TotalSellingVolume, BarDelta, DeltaSh, DeltaSl, AskImbalanceLevels, BidImbalanceLevels, AskImbalanceRatio, BidImbalanceRatio, DeltaPerVolume, PriceLevels (List\<OrderFlowPriceLevel\>) |
| `Models/EngineSettings.cs` | All 50+ tunable parameters: signal thresholds, order flow thresholds, 5 signal weights, bonuses, neutrality buffer, 18 normalization spans/baselines |
| `Ingestion/IBarBuilder.cs` | Interface: TryAddTick(tick, out bar), TryFlush(out bar) |
| `Ingestion/BarBuilder.cs` | Time-bucketed tick-to-bar conversion. Private MutableBar tracks per-price-level bid/ask via SortedDictionary. On bar boundary: finalizes bar, computes OrderFlowData (imbalance counts, ratios, delta at extremes, sorted price levels), populates PriorSignalDirection/PriorIntentScore from EngineState, calls EngineState.ApplyCompletedBar() |
| `Ingestion/OrderFlowPriceLevel.cs` | Single price level: Price, AskVolume, BidVolume, computed Delta and TotalVolume |
| `State/RollingStatistics.cs` | O(1) rolling average: fixed-capacity Queue\<double\> + running sum |
| `State/SessionContext.cs` | Intra-day session: SessionDateUtc, SessionHigh, SessionLow, SessionDelta, BarsInSession. Reset() on new trading day, Update() per bar |
| `State/EngineState.cs` | Multi-window context: VolumeStats (20-bar), RangeStats (14-bar), swing high/low queues (20-bar), SessionContext. **Also stores LastSignalDirection and LastIntentScore** via ApplySignalResult(). These are fed back into the next bar's PriorSignalDirection/PriorIntentScore for multi-bar context |
| `Signals/SignalMath.cs` | Static helpers: SafeRatio (divide-by-near-zero guard, epsilon=0.0000001), Clamp01, Clamp100 |
| `Signals/IntentSignalEngine.cs` | Core scoring: 5 signal detectors (EvaluateImbalance, EvaluateAbsorption, EvaluateFailedBreakout, EvaluateLiquiditySweep, EvaluateBreakoutContinuation) + FinalizeScores + NormalizeAbove/NormalizeBelow. Pure and stateless — takes BarData + EngineSettings, returns SignalResult |
| `Signals/SignalModels.cs` | All output types: IntentDirection (Neutral/Bullish/Bearish), IntentSignalType (5 types), IntentSignalClassification (Neutral/Continuation/Pullback/Reversal), TradeAction (StandAside/Buy/Sell), SignalFactor, SignalScore (with ScaleScore method), SignalScorePacket, SignalResult (with TrendDirection, SignalClassification, RecommendedTradeAction, EntryStyle, StopLevel, PriorSignalDirection, PriorIntentScore, GetDominantSignal with specificity ranking, ToDecisionPacket), DecisionPacket (with ToJson manual StringBuilder serializer, LatencyMs, DataQuality, TrendDirection, SignalClassification, TradeAction, EntryStyle, StopLevel) |
| `Transport/TickJsonSerializer.cs` | Static: TickData → JSON string for TCP wire format |
| `Runtime/IntentRuntime.cs` | Tick processor: routes ticks through BarBuilder, analyzes completed bars with IntentSignalEngine, tracks latency via Stopwatch, calls EngineState.ApplySignalResult(), emits barClose and/or signal packets |
| `Runtime/StreamDecision.cs` | Single emission: EventType, Bar, Result, Packet |
| `Runtime/TickProcessingResult.cs` | Per-tick output: CompletedBar + List\<StreamDecision\> Emissions |

### Intent.Console (src/Intent.Console/) — 9 files

| File | Purpose |
|------|---------|
| `Program.cs` | Entry point: parses options, creates logger/sink/archive/dashboard, wires runtime, starts server, handles Ctrl+C |
| `RunnerOptions.cs` | Config from env vars + CLI args: Host, Port, BarSeconds, TickSize, VolumeLookback, RangeLookback, StructureLookback, DefaultInstrument, LogFilePath, PacketOutputPath, TickArchiveRootPath, DashboardPort, EmitCompletedBars, EmitSignalEvents |
| `RuntimeFactory.cs` | Wires EngineSettings + EngineState + BarBuilder + IntentSignalEngine + IntentRuntime from options |
| `TcpTickServer.cs` | TCP listener: accepts connections, reads NDJSON ticks, deserializes, routes to runtime, writes emissions to stdout + sinks + dashboard. Read timeouts for responsive shutdown. Throughput metrics (totalTicksReceived, malformedTickCount, totalPacketsEmitted, ticks/sec, packets/sec) |
| `TickJsonDeserializer.cs` | DataContractJsonSerializer-based parser. Nested TickWirePayload DataContract. Accepts field variants (timestampUtc/timestamp/timeUtc, isBuyerInitiated/buyerInitiated). Validates finite prices, positive volumes. Infers bid/ask from price if missing |
| `DecisionPacketSink.cs` | Thread-safe append-only NDJSON file writer for decision packets |
| `RunnerLogger.cs` | Timestamped console + optional file logger with info/error levels |
| `RawTickArchive.cs` | Archives raw tick JSON to instrument-specific daily NDJSON files (rootDir/instrument/YYYY-MM-DD.ndjson). Lazy writer caching, filename sanitization |
| `DashboardBroadcaster.cs` | HTTP server for real-time dashboard. Endpoints: `/` (HTML dashboard UI), `/events` (SSE packet stream), `/api/status` (strategy status JSON), `/api/control` (POST commands), `/api/command` (strategy command poll), `/api/strategy-status` (strategy status push). Full HTML/CSS/JS dashboard with live metrics, bridge status age, command controls, position tracking, event log, and JSON viewer |

### Intent.Sweep (src/Intent.Sweep/) — 5 files

| File | Purpose |
|------|---------|
| `Program.cs` | Entry point: parses SweepOptions, runs ParameterSweepRunner |
| `SweepOptions.cs` | Config: InputPath, OutputPath, Mode (Combined/Imbalance/Absorption/Weights), BarSeconds, TickSize, TargetTicks, InvalidationTicks, LookaheadBars, TopCount, TrainWindowSessions. 17 parameter arrays for sweep values (each accepts comma-separated lists) |
| `ParameterSweepRunner.cs` | Core sweep engine. Builds Cartesian product of parameter configs. Walk-forward validation: train on N sessions, test on N+1. Signal evaluation: entry price ± TargetTicks*TickSize = target, ± InvalidationTicks*TickSize = invalidation. Win = target touched before invalidation within LookaheadBars. Computes precision, recall, F1, adverse excursion, time-to-move. FinalScore = F1 - (2 * stddev(fold F1s)). Ranks configs, outputs NDJSON. Separate QualityBreakdown for FULL_ORDER_FLOW vs PRICE_ONLY signals |
| `SweepSummary.cs` | Output record: all config params echoed + metrics (CompletedBars, SignalEvents, WinningSignals, FalsePositives, MissedSignals, Precision, Recall, F1, StabilityPenalty, FinalScore, AverageLatencyMs, AverageAdverseExcursionTicks, per-fold F1 scores, FullOrderFlow/PriceOnly quality breakdowns) |
| `TickFileReader.cs` | Reads NDJSON tick files (single file or directory of sessions). Returns List\<TickSession\> with parsed TickData objects |

### Intent.Replay (src/Intent.Replay/) — 3 files

| File | Purpose |
|------|---------|
| `Program.cs` | Entry point: parses ReplayOptions, runs TickReplayClient |
| `ReplayOptions.cs` | CLI args: --input (NDJSON file), --host, --port, --speed (0=instant, N=Nx playback) |
| `TickReplayClient.cs` | TCP client that replays recorded ticks from NDJSON file with timestamp-derived inter-tick delays. Uses DataContractJsonSerializer for timestamp extraction |

### Intent.Engine.Tests (src/Intent.Engine.Tests/) — 1 file

| File | Purpose |
|------|---------|
| `Program.cs` | 17 scenario-based behavioral tests with assertion helpers |

### NinjaTrader8 Indicator (src/NinjaTrader8/Indicators/) — 6 files

| File | Purpose |
|------|---------|
| `IntentLayerV01.cs` | Public indicator surface: 43+ NinjaScriptProperty parameters (all thresholds, weights, normalization spans, visual toggles, streaming config), 3 plots (IntentScore/BullScore/BearScore), bar highlighting, lifecycle (SetDefaults/Configure/DataLoaded/OnBarUpdate/OnMarketData/Terminated). Tracks previousSignalDirection and previousIntentScore for multi-bar context |
| `IntentLayerV01.Adapter.cs` | IIntentPlatformAdapter interface: BuildSettings(), BuildBarData(settings), BuildTickData(marketDataArgs) |
| `IntentLayerV01.Engine.cs` | NinjaTraderIntentAdapter: converts NinjaTrader Bars/VolumetricData into engine BarData. Counter-based price iteration for order flow extraction. Populates PriorSignalDirection/PriorIntentScore from indicator state. Computes rolling averages and prior swing levels from NinjaTrader bar series |
| `IntentLayerV01.Models.cs` | IntentVisualTheme (brushes, fonts) and IntentTags (chart drawable string constants) |
| `IntentLayerV01.Rendering.cs` | IntentChartRenderer: updates plots, bar highlighting, signal arrows per detector at offset ticks, composite BULL/BEAR text markers, fixed debug panel with full score/volume/delta/imbalance/range breakdown |
| `IntentLayerV01.Streaming.cs` | ITickStreamPublisher interface + TcpTickStreamPublisher: serializes TickData to JSON, sends over TCP to stream runner, auto-reconnect with 1s backoff, NoDelay=true |

### NinjaTrader8 Strategy (src/NinjaTrader8/Strategies/) — 1 file

| File | Purpose |
|------|---------|
| `IntentAutoTraderV01.cs` | Full automated trading strategy. 100+ NinjaScriptProperty parameters. Builds its own EngineSettings and BarData (duplicates adapter pattern for strategy independence). Full trade execution: `EnterLong` / `EnterShort`, stop/target handling, reversals, dashboard manual commands, and `5m/15m` higher-timeframe filtering. Lock reason system gates execution (`WARMUP`, `WAITING_REALTIME`, `EXECUTION_DISABLED`, `HTF_WAIT`, `HTF_NEUTRAL`, etc.). Dashboard control is HTTP-first (`/api/command`, `/api/strategy-status`) with temp-file fallback. Session P&L tracking, bridge diagnostics, and chart visualization included |
| `IntentBridgeTestStrategy.cs` | Minimal bridge-only NinjaTrader strategy for proving dashboard-driven demo orders without engine/MTF gating. Supports `set_mode`, `set_execution`, `set_dashboard_quantity`, `buy_market`, `sell_market`, `reverse`, and `flatten`, and publishes status/ack/order/execution state back to the dashboard |

---

## Signal Detection — Complete Algorithm

### Per-Detector Scoring

Each detector generates an array of SignalFactors for bullish and bearish independently:

```
SignalFactor {
    Name: string              // e.g., "Ask imbalance levels"
    RawValue: double          // e.g., 5
    NormalizedValue: double   // clamp01((value - baseline) / span) → e.g., 0.75
    Weight: double            // e.g., 35
    Contribution: double      // NormalizedValue * Weight → e.g., 26.25
}
```

Score = sum of all factor contributions, then penalties applied as multipliers, then clamped to [0, 100].

### Detector 1: Order Flow Imbalance

**With volumetric data — bullish factors:**
| Factor | Raw Source | Baseline | Span | Weight |
|--------|-----------|----------|------|--------|
| Ask imbalance levels | OrderFlow.AskImbalanceLevels | 1.0 | 4 | 35 |
| Ask imbalance ratio | OrderFlow.AskImbalanceRatio | ImbalanceRatioThreshold (2.5) | 3 | 25 |
| Delta per volume | OrderFlow.DeltaPerVolume | 0.10 | 0.40 | 20 |
| Close location | bar.CloseLocation | 0.50 | 0.50 | 20 |

**Bearish:** mirror (Bid levels/ratios, close near low via NormalizeBelow).

**Delta direction penalty:** If BarDelta <= 0, bullish *= 0.30. If BarDelta >= 0, bearish *= 0.30.

**Fallback (no volumetric data) — bullish factors:**
| Factor | Raw Source | Baseline | Span | Weight |
|--------|-----------|----------|------|--------|
| Close location | bar.CloseLocation | 0.55 | 0.45 | 40 |
| Body ratio | bar.BodyRatio | 0.35 | 0.55 | 35 |
| Volume spike | bar.VolumeSpike | 1.15 | 1.5 | 25 |

**Body direction penalty:** If Body <= 0, bullish *= 0.35. If Body >= 0, bearish *= 0.35.

### Detector 2: Absorption

**With volumetric data — bullish factors:**
| Factor | Raw Source | Type | Weight |
|--------|-----------|------|--------|
| Opposing delta | BarDelta | Directional (binary: 1.0 if delta < 0, else 0.0) | 30 |
| Delta per volume | DeltaPerVolume | NormalizeAbove from 0.22 across 0.40 | 35 |
| Price efficiency | PriceEfficiency | NormalizeBelow from 0.35 across 0.35 | 20 |
| Close location | CloseLocation | NormalizeAbove from 0.55 across 0.45 | 15 |

**Range expansion penalty:** If RangeExpansion > 1.25, both bullish and bearish *= 0.75.

**Fallback — bullish factors:**
| Factor | Raw Source | Baseline | Span | Weight |
|--------|-----------|----------|------|--------|
| Lower wick ratio | LowerWickRatio | 0.35 | 0.65 | 35 |
| Close location | CloseLocation | 0.55 | 0.45 | 25 |
| Volume spike | VolumeSpike | 1.20 | 1.5 | 25 |
| Range expansion | RangeExpansion | 1.0 | 1.5 | 15 |

**Body confirmation penalty:** If not bullish body, bullish *= 0.8. If not bearish body, bearish *= 0.8.

### Detector 3: Failed Breakout

**Bearish (failed breakout above) factors:**
| Factor | Raw Source | Baseline | Span | Weight |
|--------|-----------|----------|------|--------|
| Break above ticks | BreakAboveTicks | BreakoutExcursionTicks (2) | 8 | 35 |
| Reclaim below high | ReclaimBelowHighTicks | ReclaimTicks (1) | 8 | 25 |
| Close location | CloseLocation | NormalizeBelow from 0.55 across 0.55 | 15 |
| Bar delta confirmation | BarDelta | Directional (1.0 if delta < 0) | 10 |
| Bid imbalance levels | BidImbalanceLevels | NormalizeAbove from 1.0 across 4 | 10 |
| Breakout zone confirmation | PriceLevelConfirmation | Already normalized [0,1] | 15 |

**PriceLevelConfirmation algorithm:** Sums delta and volume at price levels above (or below) the breakout price. Computes |directionalDelta|/directionalVolume. Returns NormalizeAbove(ratio, 0.05, 0.35) only if delta direction confirms (negative above breakout = bearish confirmation, positive below = bullish).

### Detector 4: Liquidity Sweep

**Bearish (swept above prior high) factors:**
| Factor | Raw Source | Baseline | Span | Weight |
|--------|-----------|----------|------|--------|
| Break above ticks | BreakAboveTicks | BreakoutExcursionTicks (2) | 8 | 30 |
| Upper wick ratio | UpperWickRatio | SweepWickThreshold (0.40) | 0.6 | 35 |
| Volume spike | VolumeSpike | SweepVolumeSpikeThreshold (1.35) | 1.75 | 20 |
| Reclaim below high | ReclaimBelowHighTicks | ReclaimTicks (1) | 8 | 15 |
| Breakout zone confirmation | PriceLevelConfirmation | Already normalized | 10 |

### Detector 5: Breakout Continuation

Uses PriorSignalDirection and PriorIntentScore from EngineState. Detects when price breaks through a prior swing level and holds, confirming the prior signal's direction. Details are in IntentSignalEngine.cs — the 5th detector added after the original 4.

### Composite Scoring (FinalizeScores)

1. **Weighted sum** of all 5 detectors:
   - Imbalance: 0.35, Absorption: 0.20, FailedBreakout: 0.20, LiquiditySweep: 0.25
   - (BreakoutContinuation weight is also configurable via EngineSettings)

2. **Confluence bonus:** If 2+ detectors score >= SignalThreshold on same side → add ConfluenceBonus (default 8) to that side

3. **Expansive volume bonus:** If VolumeSpike >= 1.35 AND RangeExpansion >= 1.2 → add ExpansiveVolumeBonus (default 4) to both sides

4. **Direction:**
   - If |BullScore - BearScore| < NeutralityBuffer (5) OR max(bull, bear) < SignalThreshold (60): **Neutral**
   - Else: **Bullish** or **Bearish** based on higher score

5. **IntentScore** = max(BullScore, BearScore), clamped [0, 100]

6. **Signal Classification** (from SignalResult):
   - Uses PriorSignalDirection and the current Direction/DominantSignalType
   - Classifies as: Continuation (same direction as prior), Pullback (opposing but mild), Reversal (trap/sweep opposing prior trend)

7. **Trade Action:**
   - Based on Direction and SignalClassification
   - StandAside, Buy, or Sell

8. **Dominant signal selection** uses specificity ranking: OrderFlowImbalance(1) < Absorption(2) < BreakoutContinuation(3) < LiquiditySweep(4) < FailedBreakout(5). Higher-specificity signals are preferred when scores are close (within 20 points and above 60).

### Normalization Functions

```
NormalizeAbove(value, baseline, span):
    if span <= 0: return value > baseline ? 1.0 : 0.0
    return clamp01((value - baseline) / span)

NormalizeBelow(value, ceiling, span):
    if span <= 0: return value < ceiling ? 1.0 : 0.0
    return clamp01((ceiling - value) / span)
```

---

## Multi-Bar Context

The engine tracks signal persistence across bars:

1. After each bar is analyzed, `IntentRuntime` calls `EngineState.ApplySignalResult(result)` which stores `LastSignalDirection` and `LastIntentScore`
2. When `BarBuilder` finalizes the next bar, it reads these values from EngineState and populates `BarData.PriorSignalDirection` and `BarData.PriorIntentScore`
3. The signal engine uses these for:
   - Breakout continuation detection (5th detector)
   - Signal classification (Continuation/Pullback/Reversal)
   - Trade action recommendations

---

## State Management

### EngineState
- **VolumeStats** (RollingStatistics, default 20-bar): rolling average for VolumeSpike calculation
- **RangeStats** (RollingStatistics, default 14-bar): rolling average for RangeExpansion calculation
- **Swing queues** (default 20-bar): PriorSwingHigh = max of queue, PriorSwingLow = min of queue
- **SessionContext**: resets on new trading day, tracks day high/low/delta/bar count
- **LastSignalDirection / LastIntentScore**: previous bar's signal output, fed back as context

### RollingStatistics
O(1) add: Queue\<double\> + running sum. When over capacity, dequeue oldest and subtract from sum. Average = sum / count.

---

## Wire Protocol

### Inbound (tick JSON, one per line)

```json
{"timestampUtc":"2026-03-30T14:30:00.123Z","instrument":"ES 06-26","price":5425.25,"volume":12,"bid":5425.00,"ask":5425.25,"isBuyerInitiated":true}
```

Required: timestampUtc (or timestamp/timeUtc), price (finite), volume (>0). Optional: instrument, bid, ask (default to price), isBuyerInitiated (or buyerInitiated, default false).

### Outbound (decision packet JSON, one per line)

```json
{"timestampUtc":"...","instrument":"ES 06-26","session":"2026-03-30","eventType":"signal","score":84,"intentScore":84,"bullScore":84,"bearScore":41,"bias":"Bullish","direction":"Bullish","trendDirection":"Bullish","signalClassification":"Continuation","tradeAction":"Buy","entryStyle":"Follow","stopLevel":"5420.00","confidence":"HIGH","dominantReason":"Sell-side sweep and fast reclaim","dominantSignalType":"LiquiditySweep","invalidation":"Acceptance back below 5420","latencyMs":0.42,"dataQuality":"FULL_ORDER_FLOW","hasOrderFlow":true,"factors":[...],"targetZones":["prior-high:5430","bar-high:5428"],"bullishScoreFactors":[...],"bearishScoreFactors":[...],"signals":[...]}
```

Fields include: eventType (barClose/signal), confidence (HIGH>=80, MEDIUM>=60, LOW<60), dataQuality (FULL_ORDER_FLOW/PRICE_ONLY), trendDirection, signalClassification, tradeAction, entryStyle, stopLevel, latencyMs, and full factor/signal breakdowns.

---

## All Configuration Parameters

### EngineSettings (50+ parameters)

**Signal thresholds:**
SignalThreshold(60), ImbalanceVolumeSpikeThreshold(1.15), AbsorptionVolumeSpikeThreshold(1.20), AbsorptionWickThreshold(0.35), SweepVolumeSpikeThreshold(1.35), SweepWickThreshold(0.40), BreakoutExcursionTicks(2), ReclaimTicks(1)

**Order flow thresholds:**
ImbalanceRatioThreshold(2.5), AbsorptionDeltaThresholdRatio(0.22), AbsorptionPriceEfficiencyThreshold(0.35), MinImbalanceVolumePerLevel(15)

**Signal weights (should sum to ~1.0):**
ImbalanceWeight(0.35), AbsorptionWeight(0.20), FailedBreakoutWeight(0.20), LiquiditySweepWeight(0.25)

**Bonuses:**
ConfluenceBonus(8), ExpansiveVolumeBonus(4), NeutralityBuffer(5)

**Normalization spans:**
ImbalanceLevelNormalizationSpan(4), ImbalanceRatioNormalizationSpan(3), DeltaPerVolumeBaseline(0.10), DeltaPerVolumeNormalizationSpan(0.40), CloseLocationNormalizationSpan(0.50), FallbackCloseLocationNormalizationSpan(0.45), BodyRatioBaseline(0.35), BodyRatioNormalizationSpan(0.55), VolumeSpikeNormalizationSpan(1.5), AbsorptionWickNormalizationSpan(0.65), RangeExpansionPenaltyThreshold(1.25), RangeExpansionNormalizationBaseline(1.0), RangeExpansionNormalizationSpan(1.5), BreakoutNormalizationSpan(8), SweepWickNormalizationSpan(0.6), SweepVolumeNormalizationSpan(1.75), BreakoutZoneDeltaBaseline(0.05), BreakoutZoneDeltaNormalizationSpan(0.35), ExpansiveVolumeRangeExpansionThreshold(1.2)

### Console Runner (env vars / CLI args)
Host(127.0.0.1), Port(4100), BarSeconds(60), TickSize(0.25), VolumeLookback(20), RangeLookback(14), StructureLookback(20), DefaultInstrument(""), LogFilePath, PacketOutputPath, TickArchiveRootPath, DashboardPort(0), EmitCompletedBars(true), EmitSignalEvents(true)

### Auto Trader Strategy (additional parameters)
ExecutionMode(Manual/Auto), AutoTradeRealtimeOnly(true), AllowDashboardManualCommandsOutsideRealtime(true), AllowLongs/Shorts/Reversals(true), Quantity(1), UseEngineStop/UseProfitTarget(true), RewardRiskMultiple(1.5), MaxTradesPerSession(0=unlimited), CooldownBars(0), EnableChopFilter(false), CompressionRangeExpansionMax(1.05), ExpansionRangeExpansionMin(1.00), ExpansionVolumeSpikeMin(0.95), MinAutoIntentScore(45), UseDailyLossLimit(false), MaxDailyLossCurrency(200), UseFlatBeforeClose(false), FlatTime(15:55), EnableDashboardControl(true), DashboardBridgePort(4110), UseHigherTimeframeFilter(true), HigherTimeframeMinutes(15), MinHigherTimeframeIntentScore(35), TradeContinuationOnly(false), various trend/structure/breakout thresholds

### Sweep Tool (CLI args with array parameters)
InputPath, OutputPath, Mode(Combined/Imbalance/Absorption/Weights), BarSeconds(60), TickSize(0.25), TargetTicks(4), InvalidationTicks(4), LookaheadBars(8), TopCount(3), TrainWindowSessions(4), plus 17 sweepable parameter arrays accepting comma-separated values

---

## Parameter Sweep System

The sweep tool (`Intent.Sweep`) performs walk-forward cross-validation to optimize detection parameters without overfitting:

1. **Reads sessions** from NDJSON tick files (one file = one session)
2. **Builds config grid** as Cartesian product of all sweep parameter arrays
3. **Walk-forward folds:** Train on N sessions, test on N+1, slide forward
4. **Signal evaluation:** For each signal, checks if target price (entry ± TargetTicks * TickSize) is touched before invalidation price (entry ± InvalidationTicks * TickSize) within LookaheadBars
5. **Metrics:** Precision = wins/(wins+falsePositives), Recall = wins/(wins+missedSignals), F1 = harmonic mean
6. **Stability penalty:** FinalScore = F1 - (2 * stddev(fold F1 scores)) — penalizes variance across folds
7. **Ranking:** Configs ranked by FinalScore desc, then adverse excursion, time-to-move, precision, latency
8. **Quality split:** Separate metrics for FULL_ORDER_FLOW vs PRICE_ONLY signals

---

## Auto Trader Execution Logic

The strategy (`IntentAutoTraderV01`) gates every potential trade through a lock reason system:

**Lock reasons (checked in order):**
WARMUP → MANUAL_MODE → MANUAL_LOCKED → WAITING_REALTIME → EXECUTION_DISABLED → PAST_FLAT_TIME → DAILY_LOSS_LIMIT → COOLDOWN → MAX_TRADES_SESSION → NO_ANALYSIS → STAND_ASIDE → LOW_INTENT → NEUTRAL_CONTEXT → NO_DOMINANT_INTENT → CONTINUATION_ONLY → CHOP_FILTER

Only when all checks pass (lock reason = "READY") does the strategy proceed to:
1. Check AllowLongs/AllowShorts
2. Compute stop price from engine's StopLevel or bar structure
3. Compute target price = entry ± (risk * RewardRiskMultiple), rounded to tick
4. Submit entry (EnterLong/EnterShort) with configured quantity

**Compression + Expansion gate (chop filter):**
- Prior bar range <= CompressionRangeExpansionMax * AverageRange
- Current bar range >= ExpansionRangeExpansionMin * AverageRange
- Current bar volume spike >= ExpansionVolumeSpikeMin

**Dashboard control:** HTTP-first local bridge. Dashboard sends commands to `/api/control`, the strategy polls `/api/command`, and the strategy pushes status to `/api/strategy-status`. Temp-file status/heartbeat remains as fallback and diagnostics.

---

## Live Dashboard

The `DashboardBroadcaster` serves an HTTP dashboard on a configurable port with:
- **Real-time metrics** via Server-Sent Events (auto-reconnecting)
- **Trade readiness badge** (READY/MANUAL/BLOCKED with lock reason)
- **Control buttons:** Manual/Auto mode toggle, execution armed/blocked, flatten, buy/sell market, reverse, quantity set
- **Trade rules forms:** Max trades, cooldown bars, intent score threshold, compression/expansion gates, volume spike
- **Position tracking:** Entry price, stop, target, session P&L, account balance, realized/unrealized P&L
- **Event log:** 200-row rolling table of signal events
- **JSON packet viewer:** Live decision packet display

---

## Test Coverage

17 behavioral scenarios in `Intent.Engine.Tests/Program.cs`:

| Test | Validates |
|------|-----------|
| TestAbsorptionDetection | Heavy selling absorbed → bullish >= 75 |
| TestImbalanceDetection | Stacked ask levels → bullish >= 80 |
| TestFailedBreakoutTrap | Break above + reclaim → bearish >= 65 |
| TestLiquiditySweep | Sweep below + wick rejection → bullish >= 75 |
| TestBreakoutContinuation | Break + hold with prior bearish context → continuation, Sell action, "Follow" entry |
| TestNoTradeScenario | Balanced bar → Neutral, below threshold |
| TestLowQualityBreakoutDoesNotTrigger | Weak breakout → stays Neutral |
| TestScoringConsistency | Same input → identical output (deterministic) |
| TestExplainability | All reasons and factor arrays populated |
| TestStructuredDecisionPacket | Packet has all required fields, valid JSON |
| TestContinuationClassification | Prior bullish + current bullish → Continuation classification |
| TestPullbackClassification | Prior bearish + current bullish (mild) → Pullback classification |
| TestReversalClassification | Prior bearish + current bullish (trap) → Reversal classification |
| TestOrderFlowOverridesWeakBarStructure | Strong delta overrides ambiguous bar shape |
| TestEngineStateSequenceBuildsTrapContext | Multi-bar history → correct prior swing → trap fires |
| TestEngineStateSequenceBuildsSweepContext | Tick-driven 4-bar sequence → sweep detected |
| TestRuntimeHybridStreamingEmitsBarAndSignalPackets | Runtime emits barClose + signal packets with instrument |

---

## Build and Verification Toolchain

10 PowerShell scripts in `tools/`:

| Tool | Purpose |
|------|---------|
| `Run-Verification.ps1` | Full pipeline: refresh indexes → check artifacts → analyze gaps → validate architecture → compile all projects → run 17 behavior tests |
| `Validate-Behavior.ps1` | Compiles engine + tests via csc.exe, runs all 17 scenarios |
| `Validate-Architecture.ps1` | Enforces: engine has no platform deps, adapter boundary clean, rendering isolated |
| `Refresh-Indexes.ps1` | Runs Build-RepoIndex + Build-DependencyMap + Build-NinjaTraderApiIndex |
| `Build-RepoIndex.ps1` | Generates docs/REPO_INDEX.md |
| `Build-DependencyMap.ps1` | Generates docs/DEPENDENCY_MAP.md from .csproj references |
| `Build-NinjaTraderApiIndex.ps1` | Snapshots NinjaTrader API surface via reflection |
| `Analyze-Gaps.ps1` | Detects unindexed files, stale entries |
| `Check-GeneratedArtifacts.ps1` | Diffs generated docs against committed versions (timestamp-insensitive) |
| `Diff-NinjaTraderApiIndex.ps1` | Manual API version diffing utility |

---

## Data Flow Summary

```
Tick Input (JSON or NinjaTrader market data)
    │
    ▼
BarBuilder.TryAddTick() — time-buckets ticks, tracks per-price-level bid/ask
    │
    ├── same bucket → accumulate into MutableBar
    │
    └── new bucket → finalize current bar:
        │
        ▼
    MutableBar.ToBarData() — computes OrderFlowData, populates PriorSignal from EngineState
        │
        ▼
    EngineState.ApplyCompletedBar() — updates rolling stats, swing queues, session
        │
        ▼
    IntentSignalEngine.Analyze(bar, settings) — pure, stateless
        │
        ├── EvaluateImbalance()        → SignalScore (bull/bear 0-100)
        ├── EvaluateAbsorption()       → SignalScore
        ├── EvaluateFailedBreakout()   → SignalScore
        ├── EvaluateLiquiditySweep()   → SignalScore
        ├── EvaluateBreakoutCont.()    → SignalScore
        └── FinalizeScores()           → weighted sum, confluence, direction, classification
            │
            ▼
        SignalResult (5 detectors + composite + classification + trade action)
            │
            ▼
    EngineState.ApplySignalResult() — stores LastSignalDirection/LastIntentScore for next bar
            │
            ▼
    Emission: barClose packet (always) + signal packet (if >= threshold)
            │
            ├── stdout (console)
            ├── DecisionPacketSink (NDJSON file)
            ├── DashboardBroadcaster (SSE to web UI)
            └── IntentAutoTraderV01 (trade execution)
```

---

## What Exists vs. What Could Be Built Next

### Exists and Working
- Pure signal engine with 5 detectors and composite scoring
- Full explainability (factor breakdowns, reasons, confidence, classification)
- Multi-bar context (prior signal direction/score fed back into next bar)
- Signal classification (Continuation/Pullback/Reversal) with trade action recommendations
- NinjaTrader 8 indicator with all parameters exposed in UI
- NinjaTrader 8 automated trading strategy with lock reason system and chop filter
- Live web dashboard with SSE streaming, trade controls, and position tracking
- Standalone TCP streaming server with tick archiving
- Tick replay client for deterministic testing
- Walk-forward parameter sweep tool with F1 scoring and stability penalty
- 17 behavioral test scenarios including classification tests
- 10 PowerShell tools for build, verification, and documentation
- Architecture enforcement (engine purity validated automatically)

### Not Yet Built
- No persistent signal database or historical analysis storage
- No backtesting P&L framework (sweep tool evaluates signal accuracy, not portfolio performance)
- No multi-instrument correlation (each instance is independent)
- No machine learning or adaptive parameter tuning beyond grid sweep
- No alerting system (push notifications, email, webhooks)
- No market regime detection (trending vs ranging vs choppy as a first-class concept)
- No time-of-day or session-phase scoring adjustments (open vs close behavior)
- No CI/CD pipeline (verification is manual via PowerShell)
- No containerization or cloud deployment
- Graduated delta direction penalty not yet implemented (current penalty is binary 0.30 multiplier)
- Contradictory signal suppression not yet implemented (imbalance + absorption can both score high)
