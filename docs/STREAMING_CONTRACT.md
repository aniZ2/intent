# Streaming Contract

Inbound ticks to `Intent.StreamRunner` are line-delimited UTF-8 JSON objects. One TCP line is one tick.

Example:

```json
{"timestampUtc":"2026-03-30T14:30:00.0000000Z","instrument":"ES 06-26","price":100.25,"volume":12,"bid":100.00,"ask":100.25,"isBuyerInitiated":true}
```

Required fields:

- `timestampUtc`: ISO-8601 UTC timestamp
- `price`: trade price
- `volume`: trade size

Optional fields:

- `instrument`: symbol or full instrument name
- `bid`: inside bid at the time of trade
- `ask`: inside ask at the time of trade
- `isBuyerInitiated`: aggressor-side hint for order-flow attribution

Outbound decision packets are also line-delimited UTF-8 JSON.

Example:

```json
{"timestampUtc":"2026-03-30T14:31:00.0000000Z","instrument":"ES 06-26","session":"2026-03-30","eventType":"signal","score":84,"intentScore":84,"bullScore":84,"bearScore":41,"bias":"Bullish","direction":"Bullish","confidence":"HIGH","dominantReason":"Sell-side sweep and fast reclaim","dominantSignalType":"LiquiditySweep","invalidation":"Acceptance back below 99","hasOrderFlow":true,"factors":[{"name":"Imbalance weighted","rawValue":52,"normalizedValue":0.52,"weight":35,"contribution":18.2,"detail":"Weighted imbalance contribution."}],"targetZones":["prior-high:101.5","bar-high:101"],"bullishScoreFactors":[],"bearishScoreFactors":[],"signals":[]}
```

Event types:

- `barClose`: emitted for each completed bar when bar emission is enabled
- `signal`: emitted when the completed bar crosses the configured signal threshold and signal emission is enabled
