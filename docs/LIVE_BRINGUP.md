# Live Bring-Up

This is the shortest path to verify the NinjaTrader -> TCP -> Intent engine loop locally.

## 1. Build and verify once

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Run-Verification.ps1
```

## 2. Start the stream runner

This example listens on the default localhost port, writes emitted decision packets, and archives every inbound raw tick for later sweeps.

```powershell
.\src\Intent.Console\bin\Intent.StreamRunner.exe `
  --host 127.0.0.1 `
  --port 4100 `
  --tick-size 0.25 `
  --log-file .\logs\stream.log `
  --packet-file .\logs\decisions.ndjson `
  --tick-archive-dir .\data\sessions
```

Expected behavior:

- the console prints JSON `barClose` and `signal` packets
- `logs\stream.log` shows listener/client lifecycle
- raw ticks are archived under `data\sessions\<instrument>\yyyy-MM-dd.ndjson`

## 3. Import the indicator into NinjaTrader

The indicator source is under:

- `src\NinjaTrader8\Indicators\IntentLayerV01.cs`
- `src\NinjaTrader8\Indicators\IntentLayerV01.Adapter.cs`
- `src\NinjaTrader8\Indicators\IntentLayerV01.Engine.cs`
- `src\NinjaTrader8\Indicators\IntentLayerV01.Models.cs`
- `src\NinjaTrader8\Indicators\IntentLayerV01.Rendering.cs`
- `src\NinjaTrader8\Indicators\IntentLayerV01.Streaming.cs`

Compile/import the indicator as you normally would in NinjaTrader 8.

## 4. Enable streaming on the chart

On the `IntentLayerV01` indicator:

- set `EnableTickStreaming = true`
- set `StreamHost = 127.0.0.1`
- set `StreamPort = 4100`

Apply the indicator to the same instrument you want to stream.

Important:

- tick streaming only publishes `MarketDataType.Last`
- publishing happens only in `State.Realtime`
- bid/ask are tracked from `MarketDataType.Bid` and `Ask`

## 5. Confirm the connection

When NinjaTrader connects, the runner log should show:

```text
[listener] 127.0.0.1:4100 mode=hybrid
[client] connected ...
```

As ticks arrive, you should see:

- raw tick files growing under `data\sessions`
- packet JSON appended to `logs\decisions.ndjson`
- `barClose` packets each completed bar
- `signal` packets only when the threshold is crossed

## 6. Run sweeps from archived sessions

Once you have multiple daily/session files archived, run:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Run-ParameterSweep.ps1 `
  -Mode imbalance `
  -InputPath .\data\sessions\ES_06-26
```

Use a directory with at least 5 session files for walk-forward mode. Use 8+ files for a more meaningful result.

## Troubleshooting

- No connection:
  check `EnableTickStreaming`, `StreamHost`, `StreamPort`, and that the runner is already listening.
- No ticks archived:
  the chart may not be in realtime, or the instrument may not be receiving `Last` events.
- No packets emitted:
  ticks may be arriving but not enough data has accumulated to complete a bar yet.
- No `signal` packets:
  this can be normal if bars are completing but scores stay below threshold.
