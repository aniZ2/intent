#region Using declarations
using System;
using System.Collections.Generic;
using Intent.Engine.Ingestion;
using Intent.Engine.Models;
using Intent.Engine.Signals;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript.BarsTypes;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
	internal sealed class NinjaTraderIntentAdapter : IIntentPlatformAdapter
	{
		private readonly IntentLayerV01 owner;

		public NinjaTraderIntentAdapter(IntentLayerV01 owner)
		{
			this.owner = owner;
		}

		public EngineSettings BuildSettings()
		{
			return new EngineSettings
			{
				SignalThreshold = owner.SignalThreshold,
				ImbalanceVolumeSpikeThreshold = owner.ImbalanceVolumeSpikeThreshold,
				AbsorptionVolumeSpikeThreshold = owner.AbsorptionVolumeSpikeThreshold,
				AbsorptionWickThreshold = owner.AbsorptionWickThreshold,
				SweepVolumeSpikeThreshold = owner.SweepVolumeSpikeThreshold,
				SweepWickThreshold = owner.SweepWickThreshold,
				BreakoutExcursionTicks = owner.BreakoutExcursionTicks,
				ReclaimTicks = owner.ReclaimTicks,
				ImbalanceRatioThreshold = owner.ImbalanceRatioThreshold,
				AbsorptionDeltaThresholdRatio = owner.AbsorptionDeltaThresholdRatio,
				AbsorptionPriceEfficiencyThreshold = owner.AbsorptionPriceEfficiencyThreshold,
				MinImbalanceVolumePerLevel = owner.MinImbalanceVolumePerLevel,
				ImbalanceWeight = owner.ImbalanceWeight,
				AbsorptionWeight = owner.AbsorptionWeight,
				FailedBreakoutWeight = owner.FailedBreakoutWeight,
				LiquiditySweepWeight = owner.LiquiditySweepWeight,
				ConfluenceBonus = owner.ConfluenceBonus,
				ExpansiveVolumeBonus = owner.ExpansiveVolumeBonus,
				NeutralityBuffer = owner.NeutralityBuffer,
				ImbalanceLevelNormalizationSpan = owner.ImbalanceLevelNormalizationSpan,
				ImbalanceRatioNormalizationSpan = owner.ImbalanceRatioNormalizationSpan,
				DeltaPerVolumeBaseline = owner.DeltaPerVolumeBaseline,
				DeltaPerVolumeNormalizationSpan = owner.DeltaPerVolumeNormalizationSpan,
				CloseLocationNormalizationSpan = owner.CloseLocationNormalizationSpan,
				FallbackCloseLocationNormalizationSpan = owner.FallbackCloseLocationNormalizationSpan,
				BodyRatioBaseline = owner.BodyRatioBaseline,
				BodyRatioNormalizationSpan = owner.BodyRatioNormalizationSpan,
				VolumeSpikeNormalizationSpan = owner.VolumeSpikeNormalizationSpan,
				AbsorptionWickNormalizationSpan = owner.AbsorptionWickNormalizationSpan,
				RangeExpansionPenaltyThreshold = owner.RangeExpansionPenaltyThreshold,
				RangeExpansionNormalizationBaseline = owner.RangeExpansionNormalizationBaseline,
				RangeExpansionNormalizationSpan = owner.RangeExpansionNormalizationSpan,
				BreakoutNormalizationSpan = owner.BreakoutNormalizationSpan,
				SweepWickNormalizationSpan = owner.SweepWickNormalizationSpan,
				SweepVolumeNormalizationSpan = owner.SweepVolumeNormalizationSpan,
				BreakoutZoneDeltaBaseline = owner.BreakoutZoneDeltaBaseline,
				BreakoutZoneDeltaNormalizationSpan = owner.BreakoutZoneDeltaNormalizationSpan,
				ExpansiveVolumeRangeExpansionThreshold = owner.ExpansiveVolumeRangeExpansionThreshold
			};
		}

		public BarData BuildBarData(EngineSettings settings)
		{
			double high = owner.High[0];
			double low = owner.Low[0];
			double tickSize = Math.Max(owner.TickSize, 0.0000001);
			VolumetricBarsType volumetricBarsType = owner.Bars != null ? owner.Bars.BarsType as VolumetricBarsType : null;
			VolumetricData volumetricData = volumetricBarsType != null && volumetricBarsType.Volumes != null && owner.CurrentBar < volumetricBarsType.Volumes.Length
				? volumetricBarsType.Volumes[owner.CurrentBar]
				: null;

			return new BarData
			{
				TimestampUtc = owner.Time[0].ToUniversalTime(),
				Open = owner.Open[0],
				High = high,
				Low = low,
				Close = owner.Close[0],
				Volume = (long) owner.Volume[0],
				AverageVolume = AverageVolume(owner.VolumeLookback),
				AverageRange = AverageRange(owner.RangeLookback),
				PriorSwingHigh = PriorHigh(owner.StructureLookback),
				PriorSwingLow = PriorLow(owner.StructureLookback),
				PriorSignalDirection = owner.PreviousSignalDirection,
				PriorIntentScore = owner.PreviousIntentScore,
				TickSize = tickSize,
				OrderFlow = volumetricData != null
					? BuildOrderFlowData(volumetricData, low, high, tickSize, settings)
					: new OrderFlowData()
			};
		}

		public TickData BuildTickData(MarketDataEventArgs marketDataUpdate)
		{
			if (marketDataUpdate == null || marketDataUpdate.MarketDataType != MarketDataType.Last)
				return null;

			bool hasQuote = owner.LastBidPrice > 0 && owner.LastAskPrice > 0;
			double bid = owner.LastBidPrice > 0 ? owner.LastBidPrice : marketDataUpdate.Price;
			double ask = owner.LastAskPrice > 0 ? owner.LastAskPrice : marketDataUpdate.Price;
			double mid = (bid + ask) / 2.0;
			bool isBuyerInitiated = hasQuote && (marketDataUpdate.Price >= ask || (marketDataUpdate.Price > bid && marketDataUpdate.Price >= mid));

			return new TickData
			{
				TimestampUtc = marketDataUpdate.Time.ToUniversalTime(),
				Instrument = owner.Instrument == null ? string.Empty : owner.Instrument.FullName,
				Price = marketDataUpdate.Price,
				Volume = Math.Max(1, (long)marketDataUpdate.Volume),
				Bid = bid,
				Ask = ask,
				IsBuyerInitiated = isBuyerInitiated
			};
		}

		private OrderFlowData BuildOrderFlowData(VolumetricData volumetricData, double low, double high, double tickSize, EngineSettings settings)
		{
			OrderFlowData orderFlow = new OrderFlowData
			{
				IsAvailable = true,
				TotalBuyingVolume = volumetricData.TotalBuyingVolume,
				TotalSellingVolume = volumetricData.TotalSellingVolume,
				BarDelta = volumetricData.BarDelta,
				DeltaSh = volumetricData.DeltaSh,
				DeltaSl = volumetricData.DeltaSl,
				PriceLevels = new List<OrderFlowPriceLevel>()
			};

			double maxAskRatio = 0;
			double maxBidRatio = 0;

			int levelCount = (int)Math.Round((high - low) / tickSize);
			for (int levelIndex = 0; levelIndex <= levelCount; levelIndex++)
			{
				double price = low + levelIndex * tickSize;
				long askVolume = volumetricData.GetAskVolumeForPrice(price);
				long bidVolume = volumetricData.GetBidVolumeForPrice(price);
				orderFlow.PriceLevels.Add(new OrderFlowPriceLevel
				{
					Price = price,
					AskVolume = askVolume,
					BidVolume = bidVolume
				});

				if (askVolume >= settings.MinImbalanceVolumePerLevel)
				{
					double askRatio = SignalMath.SafeRatio(askVolume, Math.Max(1, bidVolume));
					maxAskRatio = Math.Max(maxAskRatio, askRatio);
					if (askRatio >= settings.ImbalanceRatioThreshold)
						orderFlow.AskImbalanceLevels++;
				}

				if (bidVolume >= settings.MinImbalanceVolumePerLevel)
				{
					double bidRatio = SignalMath.SafeRatio(bidVolume, Math.Max(1, askVolume));
					maxBidRatio = Math.Max(maxBidRatio, bidRatio);
					if (bidRatio >= settings.ImbalanceRatioThreshold)
						orderFlow.BidImbalanceLevels++;
				}
			}

			orderFlow.AskImbalanceRatio = maxAskRatio;
			orderFlow.BidImbalanceRatio = maxBidRatio;
			orderFlow.DeltaPerVolume = SignalMath.SafeRatio(Math.Abs(orderFlow.BarDelta), Math.Max(1, volumetricData.TotalVolume));
			return orderFlow;
		}

		private double AverageVolume(int lookback)
		{
			// Average of PRIOR bars (exclude the current bar), matching the strategy's baseline so the
			// charted indicator and the trading strategy compute the same VolumeSpike for a bar.
			double sum = 0;
			int bars = Math.Min(owner.CurrentBar, Math.Max(1, lookback));

			for (int barsAgo = 1; barsAgo <= bars; barsAgo++)
				sum += owner.Volume[barsAgo];

			return sum / Math.Max(1, bars);
		}

		private double AverageRange(int lookback)
		{
			// Average of PRIOR bars (exclude the current bar), matching the strategy's RangeExpansion.
			double sum = 0;
			int bars = Math.Min(owner.CurrentBar, Math.Max(1, lookback));

			for (int barsAgo = 1; barsAgo <= bars; barsAgo++)
				sum += Math.Max(owner.High[barsAgo] - owner.Low[barsAgo], owner.TickSize);

			return sum / Math.Max(1, bars);
		}

		private double PriorHigh(int lookback)
		{
			double highest = double.MinValue;
			int bars = Math.Min(owner.CurrentBar, Math.Max(1, lookback));

			for (int barsAgo = 1; barsAgo <= bars; barsAgo++)
				highest = Math.Max(highest, owner.High[barsAgo]);

			return highest == double.MinValue ? owner.High[0] : highest;
		}

		private double PriorLow(int lookback)
		{
			double lowest = double.MaxValue;
			int bars = Math.Min(owner.CurrentBar, Math.Max(1, lookback));

			for (int barsAgo = 1; barsAgo <= bars; barsAgo++)
				lowest = Math.Min(lowest, owner.Low[barsAgo]);

			return lowest == double.MaxValue ? owner.Low[0] : lowest;
		}
	}
}
