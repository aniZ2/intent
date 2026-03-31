# NinjaTrader API Index

Generated: 2026-03-30 23:23:45 -04:00

This snapshot is generated from local installed assemblies.

## NinjaTrader.NinjaScript.BarsTypes.VolumetricBarsType

Assembly: `NinjaTrader.Vendor`

### Properties

- `SkipCaching`: `Boolean`
- `Volumes`: `VolumetricData[]`

### Methods

- `ApplyDefaultBasePeriodValue(BarsPeriod period) -> Void`
- `ApplyDefaultValue(BarsPeriod period) -> Void`
- `ChartLabel(DateTime time) -> String`
- `GetInitialLookBackDays(BarsPeriod barsPeriod, TradingHours tradingHours, Int32 barsBack) -> Int32`
- `GetMaxVolumesForSession(DateTime tradingDay, Int32 barIndex) -> Int64[]`
- `GetPercentComplete(Bars bars, DateTime now) -> Double`
- `Merge(BarsType barsType) -> Void`
- `TrimEnd(Int32 numBars) -> Void`
- `TrimStart(Int32 numBars) -> Void`

## NinjaTrader.NinjaScript.BarsTypes.VolumetricData

Assembly: `NinjaTrader.Vendor`

### Properties

- `BarDelta`: `Int64`
- `CumulativeDelta`: `Int64`
- `DeltaSh`: `Int64`
- `DeltaSl`: `Int64`
- `High`: `Double`
- `Low`: `Double`
- `MaxSeenDelta`: `Int64`
- `MinSeenDelta`: `Int64`
- `TotalBuyingVolume`: `Int64`
- `TotalSellingVolume`: `Int64`
- `TotalVolume`: `Int64`
- `Trades`: `Int64`
- `Volumes`: `Int64[,]`

### Methods

- `Add(Bars bars, Double price, Int64 volume, Boolean isBuying, Int32 filter) -> Void`
- `GetAskVolumeForPrice(Double price) -> Int64`
- `GetBidVolumeForPrice(Double price) -> Int64`
- `GetDeltaForPrice(Double price) -> Int64`
- `GetDeltaPercent() -> Double`
- `GetMaximumNegativeDelta() -> Int64`
- `GetMaximumPositiveDelta() -> Int64`
- `GetMaximumVolume(Nullable`1 askVolume, Double& price) -> Int64`
- `GetTotalVolumeForPrice(Double price) -> Int64`

## NinjaTrader.NinjaScript.Indicators.OrderFlowCumulativeDelta

Assembly: `NinjaTrader.Vendor`

### Properties

- `DeltaClose`: `Series`1`
- `DeltaHigh`: `Series`1`
- `DeltaLow`: `Series`1`
- `DeltaOpen`: `Series`1`
- `DeltaType`: `CumulativeDeltaType`
- `DownBrush`: `Brush`
- `DownBrushSeralizer`: `String`
- `OutlineStroke`: `Stroke`
- `Period`: `CumulativeDeltaPeriod`
- `SizeFilter`: `Int32`
- `UpBrush`: `Brush`
- `UpBrushSeralizer`: `String`
- `WickStroke`: `Stroke`

### Methods

- `OnCalculateMinMax() -> Void`
- `OnRenderTargetChanged() -> Void`

## NinjaTrader.Data.Bars

Assembly: `NinjaTrader.Core`

### Properties

- `BarsPeriod`: `BarsPeriod`
- `BarsProxyOnChart`: `Bars`
- `BarsSeries`: `BarsSeries`
- `BarsSinceNewTradingDay`: `Int32`
- `BarsType`: `BarsType`
- `Count`: `Int32`
- `CurrentBar`: `Int32`
- `DayCount`: `Int32`
- `FromDate`: `DateTime`
- `Instrument`: `Instrument`
- `IsDividendAdjusted`: `Boolean`
- `IsFirstBarOfSession`: `Boolean`
- `IsInReplayMode`: `Boolean`
- `IsLastBarOfSession`: `Boolean`
- `IsResetOnNewTradingDay`: `Boolean`
- `IsRolloverAdjusted`: `Boolean`
- `IsSplitAdjusted`: `Boolean`
- `IsTickReplay`: `Boolean`
- `Item`: `Double`
- `LastBarTime`: `DateTime`
- `LastPrice`: `Double`
- `PercentComplete`: `Double`
- `TickCount`: `Int32`
- `ToDate`: `DateTime`
- `TotalTicks`: `Int64`
- `TradingHours`: `TradingHours`

### Methods

- `Add(Double open, Double high, Double low, Double close, DateTime time, Int64 volume, Double bid, Double ask) -> Void`
- `Add(Double open, Double high, Double low, Double close, DateTime time, Int64 volume, Double tickSize, Boolean isBar) -> Void`
- `Add(Double open, Double high, Double low, Double close, DateTime time, Int64 volume, Double tickSize, Boolean isBar, Double bid, Double ask) -> Void`
- `AddBar(Double open, Double high, Double low, Double close, DateTime time, Int64 volume, Double tickSizeIn, Double bid, Double ask) -> Void`
- `AddTest(Double open, Double high, Double low, Double close, DateTime time, Int64 volume, Double tickSize, Boolean isBar) -> Void`
- `ClearCache() -> Void`
- `Dispose() -> Void`
- `GetAsk(Int32 index) -> Double`
- `GetBar(DateTime time) -> Int32`
- `GetBarTestString(Int32 index) -> String`
- `GetBarTestString(Int32 index, Boolean ignoreIndex) -> String`
- `GetBid(Int32 index) -> Double`
- `GetClose(Int32 index) -> Double`
- `GetDayBar(Int32 tradingDaysBack) -> Bar`
- `GetHigh(Int32 index) -> Double`
- `GetIndexByTime() -> Int32`
- `GetLow(Int32 index) -> Double`
- `GetOpen(Int32 index) -> Double`
- `GetSessionEndTime(Int32 index) -> DateTime`
- `GetTime(Int32 index) -> DateTime`
- `GetValueAt(Int32 barIndex) -> Double`
- `GetVolume(Int32 index) -> Int64`
- `IsEqualBars(Bars other) -> Boolean`
- `IsEqualInstrumentBarsPeriod(Bars barsRequested) -> Boolean`
- `IsFirstBarOfSessionByIndex(Int32 index) -> Boolean`
- `IsValidDataPoint(Int32 barsAgo) -> Boolean`
- `IsValidDataPointAt(Int32 barIndex) -> Boolean`
- `RemoveLastBar() -> Void`
- `Save() -> Void`
- `SaveToFile(String path, Char separator, IProgress progress, Boolean showProgress) -> Boolean`
- `ToChartString() -> String`
- `ToString() -> String`

