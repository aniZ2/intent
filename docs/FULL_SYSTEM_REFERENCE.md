# Intent — Full System Reference

## What This Is

Intent is a real-time trading signal detection engine written in C# (.NET Framework 4.8). It analyzes price action and order flow data to detect four market microstructure patterns — order-flow imbalance, absorption, failed breakouts, and liquidity sweeps — and produces a composite directional score (0-100) with full explainability.

The system is split into a **platform-independent pure engine** and a **NinjaTrader 8 adapter**. It can run two ways:

1. **Inside NinjaTrader 8** as a chart indicator consuming live volumetric bar data
2. **Standalone** as a TCP server consuming line-delimited JSON ticks from any source

Both paths produce the same output: a `DecisionPacket` with scores, direction, confidence, dominant reason, factor breakdowns, target zones, and invalidation levels — serialized as JSON.

---

## Architecture

```
┌─────────────────────────────────────────────────────────┐
│                    Intent.Engine (pure C#)               │
│                                                         │
│  Models:    TickData, BarData, OrderFlowData,           │
│             EngineSettings, OrderFlowPriceLevel          │
│                                                         │
│  Ingestion: BarBuilder (tick-to-bar with order flow)    │
│                                                         │
│  State:     EngineState, RollingStatistics,              │
│             SessionContext                               │
│                                                         │
│  Signals:   IntentSignalEngine (4 detectors + scoring)  │
│             SignalMath (safe math helpers)               │
│             SignalModels (results, factors, packets)     │
│                                                         │
│  Runtime:   IntentRuntime (tick processor + emitter)     │
│                                                         │
│  Transport: TickJsonSerializer                           │
└────────────┬──────────────────────────┬─────────────────┘
             │                          │
    ┌────────▼────────┐       ┌────────▼──────────────┐
    │ Intent.Console  │       │ NinjaTrader8 Adapter   │
    │                 │       │                        │
    │ TcpTickServer   │       │ IntentLayerV01.cs      │
    │ TickJsonDeser.  │       │   (indicator surface)  │
    │ DecisionPacket  │       │ IntentLayerV01.Engine   │
    │   Sink          │       │   (data conversion)    │
    │ RunnerLogger    │       │ IntentLayerV01.Adapter  │
    │ RuntimeFactory  │       │   (interface contract) │
    │ RunnerOptions   │       │ IntentLayerV01.Render.  │
    └─────────────────┘       │   (chart visualization)│
                              │ IntentLayerV01.Stream.  │
    ┌─────────────────┐       │   (tick TCP publisher) │
    │ Intent.Replay   │       │ IntentLayerV01.Models   │
    │                 │       │   (visual theme)       │
    │ TickReplayClient│       └────────────────────────┘
    │ ReplayOptions   │
    └─────────────────┘

    ┌─────────────────┐
    │ Intent.Engine   │
    │   .Tests        │
    │                 │
    │ 14 behavioral   │
    │ test scenarios  │
    └─────────────────┘
```

**Dependency graph (one-directional, no cycles):**

- `Intent.Engine` depends on nothing (pure library)
- `Intent.Console` depends on `Intent.Engine`
- `Intent.Replay` depends on nothing (standalone TCP client)
- `Intent.Engine.Tests` depends on `Intent.Engine`
- `NinjaTrader8/IntentLayerV01` depends on `Intent.Engine` + NinjaTrader platform APIs

---

## Projects and Every File

### Intent.Engine (pure library, no platform dependencies)

| File | Purpose |
|------|---------|
| `Models/TickData.cs` | Single trade: timestamp, price, volume, bid, ask, buyer-initiated flag |
| `Models/BarData.cs` | Completed bar with OHLCV + 20 computed properties (range, body, wicks, ratios, volume spike, range expansion, break distances, reclaim distances, close location, price efficiency) |
| `Models/OrderFlowData.cs` | Volumetric snapshot: total buying/selling volume, bar delta, delta at high/low, imbalance level counts, imbalance ratios, delta-per-volume, per-price-level breakdown |
| `Models/EngineSettings.cs` | All 50+ tunable parameters: signal thresholds, order flow thresholds, signal weights, bonuses, and 18 normalization spans/baselines |
| `Ingestion/IBarBuilder.cs` | Interface: TryAddTick, TryFlush |
| `Ingestion/BarBuilder.cs` | Converts tick stream to bars via time-bucketing. Tracks per-price-level bid/ask volume inside a private MutableBar. On bar boundary crossing: finalizes bar, computes order flow metrics (imbalance counts, ratios, delta at extremes), updates EngineState, returns completed BarData |
| `Ingestion/OrderFlowPriceLevel.cs` | Single price level: price, ask volume, bid volume, computed delta and total |
| `State/RollingStatistics.cs` | O(1) rolling average using a queue + running sum. Capacity-limited. Used for volume and range averages |
| `State/SessionContext.cs` | Intra-day session state: session date, session high/low, cumulative delta, bar count. Resets on new trading day |
| `State/EngineState.cs` | Multi-window context: RollingStatistics for volume (20-bar) and range (14-bar), SessionContext, prior swing high/low from structure lookback (20-bar queue) |
| `Signals/SignalMath.cs` | Static helpers: SafeRatio (divide-by-near-zero guard), Clamp01, Clamp100 |
| `Signals/IntentSignalEngine.cs` | The core: 4 signal detectors + composite scoring + normalization functions. Pure, stateless — takes a BarData and EngineSettings, returns a SignalResult |
| `Signals/SignalModels.cs` | All output types: SignalFactor (name, raw, normalized, weight, contribution, detail), SignalScore (per-detector bull/bear + reasons + factors), SignalResult (4 detectors + composite + direction), DecisionPacket (wire-ready JSON with manual StringBuilder serializer) |
| `Transport/TickJsonSerializer.cs` | Serializes TickData to JSON string for TCP wire format |
| `Runtime/IntentRuntime.cs` | Tick processor: routes ticks through BarBuilder, analyzes completed bars with IntentSignalEngine, emits barClose and/or signal packets based on configuration |
| `Runtime/StreamDecision.cs` | Single emission: event type (barClose/signal), bar, analysis result, decision packet |
| `Runtime/TickProcessingResult.cs` | Per-tick output: completed bar (if any) + list of emissions (0-2 packets) |

### Intent.Console (standalone TCP runner)

| File | Purpose |
|------|---------|
| `Program.cs` | Entry point: parses RunnerOptions, wires RuntimeFactory, starts TcpTickServer, handles Ctrl+C graceful shutdown |
| `RunnerOptions.cs` | Configuration from environment variables or CLI args: host, port, bar-seconds, tick-size, instrument, log-file, packet-file, emit-signals-only, emit-bars-only |
| `RuntimeFactory.cs` | Wires EngineSettings + EngineState + BarBuilder + IntentSignalEngine + IntentRuntime from RunnerOptions |
| `TcpTickServer.cs` | TCP listener: accepts connections, reads NDJSON ticks line-by-line, deserializes, routes to IntentRuntime, writes emissions to stdout + optional DecisionPacketSink. Uses read timeouts for responsive shutdown. Tracks throughput metrics (ticks received, malformed count, packets emitted, ticks/sec, packets/sec) |
| `TickJsonDeserializer.cs` | Parses JSON tick payloads using DataContractJsonSerializer. Accepts field name variants (timestampUtc/timestamp/timeUtc, isBuyerInitiated/buyerInitiated). Validates finite prices, positive volumes. Infers bid/ask from price if missing |
| `DecisionPacketSink.cs` | Append-only NDJSON file writer for decision packets. Thread-safe, auto-flushed, creates directory if needed |
| `RunnerLogger.cs` | Timestamped console + optional file logger |

### Intent.Replay (tick replay client)

| File | Purpose |
|------|---------|
| `Program.cs` | Entry point: parses ReplayOptions, runs TickReplayClient |
| `ReplayOptions.cs` | CLI args: --input (NDJSON file), --host, --port, --speed (0=instant, N=Nx playback) |
| `TickReplayClient.cs` | Reads NDJSON tick file, extracts timestamps, calculates inter-tick delays, connects to stream runner TCP port, replays ticks at configured speed. Reports tick count |

### Intent.Engine.Tests (behavioral validation)

| File | Purpose |
|------|---------|
| `Program.cs` | 14 scenario-based tests validating all signal detectors, composite scoring, state accumulation, packet serialization, explainability, and runtime streaming. Uses assertion helpers that report pass/fail with descriptive messages |

### NinjaTrader8/Indicators (platform adapter, 5 partial class files + 1 interface)

| File | Purpose |
|------|---------|
| `IntentLayerV01.cs` | Public indicator surface: 43+ NinjaScriptProperty parameters (thresholds, weights, normalization spans, visual toggles, streaming config), 3 plots (IntentScore, BullScore, BearScore), bar highlighting, lifecycle management (SetDefaults, Configure, DataLoaded, OnBarUpdate, OnMarketData, Terminated) |
| `IntentLayerV01.Adapter.cs` | IIntentPlatformAdapter interface contract: BuildSettings(), BuildBarData(settings), BuildTickData(marketDataArgs). Clean boundary between NinjaTrader APIs and engine |
| `IntentLayerV01.Engine.cs` | NinjaTraderIntentAdapter implementation: converts NinjaTrader Bars/VolumetricData into engine BarData. Extracts order flow from VolumetricBarsType using counter-based price iteration. Computes rolling averages and prior swing levels from NinjaTrader bar series |
| `IntentLayerV01.Models.cs` | IntentVisualTheme: brushes and fonts for chart rendering. IntentTags: string constants for drawable objects |
| `IntentLayerV01.Rendering.cs` | IntentChartRenderer: updates plots, applies bar brushes, draws signal arrows (up/down per detector at offset ticks), draws composite BULL/BEAR text markers, renders fixed debug panel showing all scores, order flow metrics, and per-detector breakdowns |

---

## How Signal Detection Works

### The Four Detectors

Each detector independently scores bullish (0-100) and bearish (0-100) using weighted factors. Every factor has: a raw value, a normalized value (0-1), a weight, and a contribution (normalized * weight).

#### 1. Order Flow Imbalance

**What it detects:** Stacked aggressive volume on one side across multiple price levels, with directional confirmation from delta and close location.

**Bullish factors (with volumetric data):**
- Ask imbalance level count (how many levels have ask/bid ratio >= 2.5)
- Ask imbalance ratio (max ask/bid ratio across all levels)
- Delta per volume (|bar delta| / total volume — directional conviction)
- Close location (where price closed within bar range, 1.0 = at high)

**Directional penalty:** If bar delta is negative (sellers dominated), bullish score is multiplied by 0.30. If bar delta is positive, bearish score is multiplied by 0.30.

**Fallback (no volumetric data):** Uses bar structure — close location, body ratio, volume spike. Weaker but functional. Body direction confirmation penalty (0.35 multiplier if body contradicts signal direction).

#### 2. Absorption

**What it detects:** One side absorbing the other's aggression — heavy volume on one side but price doesn't move in that direction. Shows as a wick (tail) on the bar.

**Bullish factors (with volumetric data):**
- Opposing delta (selling pressure, but absorbed — bar delta negative)
- Delta per volume (strength of the absorption)
- Price efficiency inverted (lower = more absorption, price didn't follow the aggressor)
- Close location (held near highs despite selling pressure)

**Expansion penalty:** If range expansion > 1.25x average, score is multiplied by 0.75 (absorption is less reliable on oversized bars).

**Fallback:** Uses wick ratios (lower wick for bullish absorption, upper for bearish), close location, volume spike, range expansion. Body direction confirmation penalty (0.8 multiplier).

#### 3. Failed Breakout

**What it detects:** Price breaks beyond a prior swing level (high or low) then reverses and closes back inside. This is a trap — breakout traders are caught wrong-footed.

**Bearish trap factors (bearish signal — failed breakout above):**
- Break above distance in ticks (how far price exceeded prior swing high)
- Reclaim below high in ticks (how far price closed back below swing high)
- Close location (closed in lower portion of bar range)
- Breakout zone delta (if volumetric data: negative delta in the zone above the prior high confirms sellers stepped in)

**Bullish trap factors:** Mirror (break below prior low, reclaim above).

**Qualifying thresholds:** Break must exceed `BreakoutExcursionTicks` (default 2) and reclaim must exceed `ReclaimTicks` (default 1).

#### 4. Liquidity Sweep

**What it detects:** Price sweeps through a prior swing level into a low-volume zone, then snaps back with a rejection wick and elevated volume. Indicates stop-hunting or liquidity-taking.

**Bullish factors (bearish sweep — swept below prior low):**
- Break below distance in ticks
- Lower wick ratio (rejection wick size relative to bar range)
- Volume spike (elevated participation into the sweep)
- Reclaim above low in ticks (fast snap back)
- Breakout zone delta (positive delta in zone below prior low — buyers stepping in)

**Bearish factors:** Mirror (swept above prior high).

### Composite Scoring

After all four detectors produce their individual bullish and bearish scores:

1. **Weighted sum** — each detector's score is weighted:
   - Imbalance: 0.35 (35%)
   - Absorption: 0.20 (20%)
   - Failed Breakout: 0.20 (20%)
   - Liquidity Sweep: 0.25 (25%)

2. **Confluence bonus** — if 2 or more detectors fire on the same side (score >= that detector's internal trigger level), add +8 points to that side

3. **Expansive volume bonus** — if volume spike >= 1.35x AND range expansion >= 1.2x, add +4 points to both sides (strong participation confirmation)

4. **Direction determination:**
   - If `|bullScore - bearScore| < 5` (neutrality buffer): **Neutral**
   - If `max(bullScore, bearScore) < 60` (signal threshold): **Neutral**
   - Otherwise: **Bullish** or **Bearish** based on which score is higher

5. **IntentScore** = max(bullScore, bearScore), clamped to 0-100

### Normalization Functions

Two core normalizers used throughout:

**NormalizeAbove(value, baseline, span):** For metrics where higher = stronger signal.
`result = clamp01((value - baseline) / span)`. If span is 0, returns binary 0 or 1.

**NormalizeBelow(value, ceiling, span):** For metrics where lower = stronger signal.
`result = clamp01((ceiling - value) / span)`. If span is 0, returns binary 0 or 1.

All 18 normalization spans and baselines are configurable in EngineSettings and exposed in the NinjaTrader UI.

---

## Data Flow: Tick to Signal

### Standalone TCP Path

```
External data source
    │ (TCP connection to host:port)
    │
    ▼
TcpTickServer.HandleClient()
    │ reads line-delimited JSON
    │
    ▼
TickJsonDeserializer.TryDeserialize(line)
    │ validates fields, produces TickData
    │
    ▼
IntentRuntime.OnTick(tick)
    │
    ▼
BarBuilder.TryAddTick(tick)
    │ time-aligns tick to bar bucket
    │ accumulates into MutableBar
    │ tracks per-price-level bid/ask
    │
    ├── same bar bucket → accumulate, return false
    │
    └── new bar bucket → finalize current bar:
        │
        ▼
        MutableBar.ToBarData()
            │ computes OrderFlowData:
            │   - imbalance level counts
            │   - max imbalance ratios
            │   - delta at high/low
            │   - delta per volume
            │   - sorted price levels
            │ computes bar structure:
            │   - range, body, wicks, ratios
            │   - volume spike, range expansion
            │   - break/reclaim distances
            │
            ▼
        EngineState.ApplyCompletedBar(bar)
            │ updates RollingStatistics (volume, range)
            │ updates swing high/low queues
            │ resets SessionContext if new day
            │
            ▼
        IntentSignalEngine.Analyze(bar, settings)
            │
            ├── EvaluateImbalance()
            ├── EvaluateAbsorption()
            ├── EvaluateFailedBreakout()
            ├── EvaluateLiquiditySweep()
            └── FinalizeScores()
                │
                ▼
            SignalResult
                │ IntentScore, BullScore, BearScore
                │ Direction (Neutral/Bullish/Bearish)
                │ DominantReason, per-detector scores
                │ Full factor breakdowns
                │
                ▼
            Emission check:
                │
                ├── barClose packet (if enabled)
                │     → stdout + DecisionPacketSink
                │
                └── signal packet (if IntentScore >= threshold)
                      → stdout + DecisionPacketSink
```

### NinjaTrader Path

```
NinjaTrader platform
    │ OnBarUpdate() fires on each bar close
    │
    ▼
IntentLayerV01.OnBarUpdate()
    │
    ▼
NinjaTraderIntentAdapter.BuildSettings()
    │ maps 43+ indicator properties to EngineSettings
    │
    ▼
NinjaTraderIntentAdapter.BuildBarData(settings)
    │ reads current bar OHLCV from NinjaTrader Bars series
    │ attempts VolumetricBarsType cast for Order Flow+ data
    │ if available: BuildOrderFlowData() extracts per-level volumes
    │ computes rolling averages from lookback windows
    │ computes prior swing high/low from structure lookback
    │
    ▼
IntentSignalEngine.Analyze(bar, settings)
    │ (same pure engine path as standalone)
    │
    ▼
IntentChartRenderer.Render(bar, analysis, ...)
    │ updates IntentScore/BullScore/BearScore plots
    │ applies bar highlighting (green=bullish, red=bearish)
    │ draws signal arrows per detector
    │ draws composite BULL/BEAR text markers
    │ renders debug panel with full breakdown
```

**Optional tick streaming from NinjaTrader:**

```
NinjaTrader platform
    │ OnMarketData() fires on every tick
    │
    ▼
IntentLayerV01.OnMarketData()
    │ tracks lastBidPrice, lastAskPrice
    │ on Last trade: BuildTickData()
    │
    ▼
TcpTickStreamPublisher.Publish(tick)
    │ serializes to JSON via TickJsonSerializer
    │ sends over TCP to stream runner
    │ auto-reconnects on failure
```

---

## Wire Protocol

### Inbound (tick JSON, line-delimited)

```json
{"timestampUtc":"2026-03-30T14:30:00.123Z","instrument":"ES 06-26","price":5425.25,"volume":12,"bid":5425.00,"ask":5425.25,"isBuyerInitiated":true}
```

| Field | Required | Type | Notes |
|-------|----------|------|-------|
| timestampUtc | yes | string | ISO-8601 UTC. Also accepts `timestamp` or `timeUtc` |
| price | yes | number | Must be finite |
| volume | yes | integer | Must be > 0 |
| instrument | no | string | Defaults to configured instrument |
| bid | no | number | Defaults to price |
| ask | no | number | Defaults to price |
| isBuyerInitiated | no | boolean | Also accepts `buyerInitiated`. Defaults to false |

### Outbound (decision packet JSON, line-delimited)

```json
{
  "timestampUtc": "2026-03-30T14:31:00.000Z",
  "instrument": "ES 06-26",
  "session": "2026-03-30",
  "eventType": "signal",
  "score": 84,
  "intentScore": 84,
  "bullScore": 84,
  "bearScore": 41,
  "bias": "Bullish",
  "direction": "Bullish",
  "confidence": "HIGH",
  "dominantReason": "Sell-side sweep and fast reclaim",
  "dominantSignalType": "LiquiditySweep",
  "invalidation": "Acceptance back below 99.7500",
  "hasOrderFlow": true,
  "targetZones": ["prior-high:101.50", "bar-high:101.00"],
  "factors": [
    {"name": "Break below ticks", "rawValue": 4.0, "normalizedValue": 0.50, "weight": 25.0, "contribution": 12.50, "detail": "..."}
  ],
  "bullishScoreFactors": [...],
  "bearishScoreFactors": [...],
  "signals": [
    {"signalType": "OrderFlowImbalance", "bullish": 52.0, "bearish": 12.0, "bullishReason": "...", "bearishReason": "..."},
    {"signalType": "Absorption", "bullish": 0.0, "bearish": 0.0, ...},
    {"signalType": "FailedBreakout", "bullish": 0.0, "bearish": 0.0, ...},
    {"signalType": "LiquiditySweep", "bullish": 85.0, "bearish": 0.0, ...}
  ]
}
```

| Field | Type | Description |
|-------|------|-------------|
| eventType | string | `barClose` (every completed bar) or `signal` (only when IntentScore >= threshold) |
| score / intentScore | number | max(bullScore, bearScore), 0-100 |
| confidence | string | HIGH (>= 80), MEDIUM (>= 60), LOW (< 60) |
| invalidation | string | Price level where the signal thesis breaks |
| targetZones | string[] | Key levels: prior-high, prior-low, bar-high, bar-low |
| factors | object[] | Factor breakdown for the winning direction |
| signals | object[] | All 4 detector scores with reasons |

---

## Configuration Reference

### Console Runner (environment variables / CLI args)

| Setting | Env Var | CLI Flag | Default |
|---------|---------|----------|---------|
| Host | INTENT_STREAM_HOST | --host | 127.0.0.1 |
| Port | INTENT_STREAM_PORT | --port | 4100 |
| Bar size (seconds) | INTENT_STREAM_BAR_SECONDS | --bar-seconds | 60 |
| Tick size | INTENT_STREAM_TICK_SIZE | --tick-size | 0.25 |
| Instrument | INTENT_STREAM_INSTRUMENT | --instrument | (empty) |
| Log file | INTENT_STREAM_LOG_FILE | --log-file | (none) |
| Packet file | INTENT_STREAM_PACKET_FILE | --packet-file | (none) |
| Signals only | — | --emit-signals-only | false |
| Bars only | — | --emit-bars-only | false |

### Replay Client (CLI args only)

| Flag | Required | Default | Description |
|------|----------|---------|-------------|
| --input | yes | — | Path to NDJSON tick file |
| --host | no | 127.0.0.1 | Stream runner host |
| --port | no | 4100 | Stream runner port |
| --speed | no | 0 | Playback multiplier (0 = instant, 20 = 20x real time) |

### EngineSettings (all tunable parameters)

**Signal thresholds:**

| Parameter | Default | Description |
|-----------|---------|-------------|
| SignalThreshold | 60 | Minimum IntentScore to emit a signal packet |
| ImbalanceVolumeSpikeThreshold | 1.15 | Volume/avgVolume minimum for imbalance |
| AbsorptionVolumeSpikeThreshold | 1.20 | Volume/avgVolume minimum for absorption |
| AbsorptionWickThreshold | 0.35 | Wick ratio minimum for absorption fallback |
| SweepVolumeSpikeThreshold | 1.35 | Volume/avgVolume minimum for sweep |
| SweepWickThreshold | 0.40 | Wick ratio minimum for sweep |
| BreakoutExcursionTicks | 2 | Ticks beyond prior swing to qualify as breakout |
| ReclaimTicks | 1 | Ticks reclaimed inside prior swing for trap/sweep |

**Order flow thresholds:**

| Parameter | Default | Description |
|-----------|---------|-------------|
| ImbalanceRatioThreshold | 2.5 | Ask/bid ratio to count as imbalance at a price level |
| AbsorptionDeltaThresholdRatio | 0.22 | |delta|/volume for absorption detection |
| AbsorptionPriceEfficiencyThreshold | 0.35 | Max price efficiency for absorption |
| MinImbalanceVolumePerLevel | 15 | Minimum volume at a level to consider for imbalance |

**Signal weights (must sum to 1.0):**

| Parameter | Default | Description |
|-----------|---------|-------------|
| ImbalanceWeight | 0.35 | Imbalance detector contribution to composite |
| AbsorptionWeight | 0.20 | Absorption detector contribution |
| FailedBreakoutWeight | 0.20 | Failed breakout detector contribution |
| LiquiditySweepWeight | 0.25 | Liquidity sweep detector contribution |

**Bonuses:**

| Parameter | Default | Description |
|-----------|---------|-------------|
| ConfluenceBonus | 8 | Points added when 2+ detectors fire same direction |
| ExpansiveVolumeBonus | 4 | Points added when volume spike + range expansion both present |
| NeutralityBuffer | 5 | Minimum bull-bear score difference to avoid Neutral |

**Normalization spans (control how raw values map to 0-1):**

| Parameter | Default | Controls |
|-----------|---------|----------|
| ImbalanceLevelNormalizationSpan | 4 | Imbalance level count range |
| ImbalanceRatioNormalizationSpan | 3 | Imbalance ratio range |
| DeltaPerVolumeBaseline | 0.10 | Delta/volume baseline |
| DeltaPerVolumeNormalizationSpan | 0.40 | Delta/volume range |
| CloseLocationNormalizationSpan | 0.50 | Close location range (volumetric path) |
| FallbackCloseLocationNormalizationSpan | 0.45 | Close location range (bar-only path) |
| BodyRatioBaseline | 0.35 | Body ratio baseline |
| BodyRatioNormalizationSpan | 0.55 | Body ratio range |
| VolumeSpikeNormalizationSpan | 1.5 | Volume spike range |
| AbsorptionWickNormalizationSpan | 0.65 | Wick ratio range for absorption |
| RangeExpansionPenaltyThreshold | 1.25 | Range expansion above this penalizes absorption |
| RangeExpansionNormalizationBaseline | 1.0 | Range expansion baseline |
| RangeExpansionNormalizationSpan | 1.5 | Range expansion range |
| BreakoutNormalizationSpan | 8 | Break/reclaim tick count range |
| SweepWickNormalizationSpan | 0.6 | Sweep wick ratio range |
| SweepVolumeNormalizationSpan | 1.75 | Sweep volume spike range |
| BreakoutZoneDeltaBaseline | 0.05 | Zone delta baseline |
| BreakoutZoneDeltaNormalizationSpan | 0.35 | Zone delta range |
| ExpansiveVolumeRangeExpansionThreshold | 1.2 | Range expansion threshold for volume bonus |

---

## State Management

### EngineState

Maintains rolling context across bars. Created once per runtime/indicator instance.

- **VolumeStats** (RollingStatistics, 20-bar window): Running average of bar volumes. Used to compute VolumeSpike = currentVolume / averageVolume.
- **RangeStats** (RollingStatistics, 14-bar window): Running average of bar ranges. Used to compute RangeExpansion = currentRange / averageRange.
- **Structure queue** (20-bar window): Stores recent bar highs and lows. PriorSwingHigh = max of queue highs. PriorSwingLow = min of queue lows. Used for breakout and sweep distance calculations.
- **SessionContext**: Tracks current trading day high/low, cumulative delta, bar count. Resets when bar timestamp crosses to a new date.

### RollingStatistics

O(1) add and average. Uses a fixed-capacity Queue<double> and a running sum. When a new value is added and the queue exceeds capacity, the oldest value is dequeued and subtracted from the sum.

---

## Test Coverage

14 behavioral scenarios in `Intent.Engine.Tests/Program.cs`:

| Test | What It Validates |
|------|-------------------|
| TestAbsorptionDetection | Heavy selling absorbed, bullish score >= 75 |
| TestImbalanceDetection | Stacked ask levels, bullish score >= 80 |
| TestFailedBreakoutTrap | Break above + reclaim, bearish score >= 65 |
| TestLiquiditySweep | Sweep below + wick rejection, bullish score >= 75 |
| TestNoTradeScenario | Balanced bar stays Neutral, below threshold |
| TestScoringConsistency | Same input produces identical output (deterministic) |
| TestLowQualityBreakoutDoesNotTrigger | Weak breakout rejected, stays Neutral |
| TestExplainability | All reasons and factor arrays populated |
| TestStructuredDecisionPacket | Packet has direction, confidence, targets, factors, JSON keys |
| TestOrderFlowOverridesWeakBarStructure | Strong delta overrides ambiguous bar shape |
| TestEngineStateSequenceBuildsTrapContext | Multi-bar history builds prior swing, then trap fires |
| TestEngineStateSequenceBuildsSweepContext | Tick-driven 4-bar sequence builds sweep context |
| TestRuntimeHybridStreamingEmitsBarAndSignalPackets | Runtime emits both barClose and signal packets with instrument |
| TestTickDrivenBarBuilderProducesOrderFlow | Tick-level ingestion builds correct order flow from buyer-initiated flags |

---

## Build and Verification Toolchain

All tools are PowerShell scripts in `tools/`. The primary entry point is `Run-Verification.ps1`.

| Tool | Purpose |
|------|---------|
| `Run-Verification.ps1` | Full pipeline: refresh indexes → check artifacts → analyze gaps → validate architecture → compile all projects (engine DLL, console EXE, replay EXE, NinjaTrader DLL) → run behavior tests |
| `Validate-Behavior.ps1` | Compiles Intent.Engine + Intent.Engine.Tests via csc.exe and runs all 14 test scenarios |
| `Validate-Architecture.ps1` | Enforces separation: engine has no platform references, NinjaTrader adapter only references engine, rendering doesn't touch volumetric APIs directly |
| `Refresh-Indexes.ps1` | Orchestrates: Build-RepoIndex + Build-DependencyMap + Build-NinjaTraderApiIndex |
| `Build-RepoIndex.ps1` | Generates docs/REPO_INDEX.md — file listing with per-file descriptions |
| `Build-DependencyMap.ps1` | Generates docs/DEPENDENCY_MAP.md — project reference graph from .csproj files |
| `Build-NinjaTraderApiIndex.ps1` | Generates docs/NINJATRADER_API_INDEX.md — NinjaTrader API surface snapshot via reflection |
| `Analyze-Gaps.ps1` | Detects unindexed files, stale entries, and generic summaries in REPO_INDEX |
| `Check-GeneratedArtifacts.ps1` | Regenerates all indexes in temp directory and diffs against committed versions (timestamp-insensitive) |
| `Diff-NinjaTraderApiIndex.ps1` | Manual utility to diff two NinjaTrader API snapshots for version tracking |

### Standalone compilation (no MSBuild/Visual Studio required)

All projects compile via `csc.exe` (C# compiler from .NET Framework 4.0):

```
# Engine library
csc.exe /target:library /out:bin/Intent.Engine.dll src/Intent.Engine/**/*.cs

# Console runner
csc.exe /target:exe /r:Intent.Engine.dll /r:System.Runtime.Serialization.dll /out:bin/Intent.StreamRunner.exe src/Intent.Console/*.cs

# Replay client
csc.exe /target:exe /r:System.Runtime.Serialization.dll /out:bin/Intent.Replay.exe src/Intent.Replay/*.cs

# NinjaTrader indicator (requires NinjaTrader assemblies)
csc.exe /target:library /r:Intent.Engine.dll /r:NinjaTrader.*.dll /out:bin/IntentLayerV01.dll src/NinjaTrader8/Indicators/*.cs

# Tests
csc.exe /target:exe /r:Intent.Engine.dll /out:bin/Intent.Engine.Tests.exe src/Intent.Engine.Tests/Program.cs
```

---

## NinjaTrader Chart Output

When running inside NinjaTrader, the indicator renders:

**Three plots (in separate panel):**
- IntentScore (DodgerBlue): 0-100 composite
- BullScore (ForestGreen): 0-100 weighted bullish
- BearScore (IndianRed): 0-100 weighted bearish

**Bar highlighting:**
- Green bars when Direction = Bullish and IntentScore >= threshold
- Red bars when Direction = Bearish and IntentScore >= threshold

**Signal arrows (on price chart):**
- Up/down arrows per detector at offset ticks (1-4 ticks from bar extreme)
- Offset 1: Imbalance, Offset 2: Absorption, Offset 3: Failed Breakout, Offset 4: Liquidity Sweep

**Composite text markers:**
- "BULL" or "BEAR" text at 5-tick offset from bar extreme

**Debug panel (fixed position on chart):**
```
IntentLayerV01
Score 84  Bull 84  Bear 41
Bias  Bullish
Lead  Sell-side sweep and fast reclaim
Vol   1500  Avg 900  Spike 1.67x
OF    D +120  Ask 1200  Bid 300
Imb   Ask 5 (6.00)  Bid 1 (1.00)
Rng   1.25  Avg 0.90  Exp 1.39x
Imb   B 80 | S 0
Abs   B 72 | S 0
Fail  B 0 | S 0
Sweep B 85 | S 0
```

---

## What Exists vs. What Is Missing

### Exists and Working
- Pure signal detection engine with 4 detectors and composite scoring
- Full explainability (factor breakdowns, reasons, confidence levels)
- NinjaTrader 8 adapter with volumetric order flow extraction
- Standalone TCP streaming server with NDJSON protocol
- Tick replay client for deterministic testing
- 14 behavioral test scenarios
- 10 PowerShell tools for build, verification, and documentation
- Architecture enforcement (engine purity validated automatically)
- Generated documentation kept in sync with source

### Not Yet Built
- No persistent storage or historical signal database
- No backtesting framework (replay client streams forward only, no P&L tracking)
- No multi-instrument correlation (each instance analyzes one instrument independently)
- No machine learning or adaptive parameter tuning
- No web UI or dashboard (output is chart-based or NDJSON files)
- No alerting system (no push notifications, email, or webhook on signal)
- No position sizing, risk management, or trade execution logic
- No market regime detection (trending vs. ranging vs. choppy)
- No time-of-day or session-phase awareness in scoring (e.g., open vs. close behavior)
- No integration tests for the TCP server (behavioral tests cover the engine, not the network layer)
- No test coverage for Intent.Replay (TickReplayClient)
- No CI/CD pipeline (verification is manual via PowerShell)
- No containerization or deployment automation
- No configuration file support for the engine (settings are code-level or NinjaTrader UI properties)
- Normalization spans are not yet wired through the NinjaTrader adapter UI (engine uses constructor defaults; the 18 normalization properties exist in EngineSettings and IntentLayerV01.cs but BuildSettings() in the adapter may not map all of them)
