using System;
using System.Collections.Generic;
using Intent.Engine.Models;
using Intent.Engine.Signals;
using Intent.Engine.State;

namespace Intent.Engine.Ingestion
{
	public sealed class BarBuilder : IBarBuilder
	{
		private readonly TimeSpan barSize;
		private readonly EngineState engineState;
		private readonly double tickSize;
		private DateTime currentBarStartUtc;
		private MutableBar currentBar;

		public BarBuilder(TimeSpan barSize, EngineState engineState, double tickSize)
		{
			this.barSize = barSize;
			this.engineState = engineState;
			this.tickSize = tickSize <= 0 ? 0.25 : tickSize;
		}

		public bool TryAddTick(TickData tick, out BarData completedBar)
		{
			completedBar = null;
			DateTime bucket = AlignTimestamp(tick.TimestampUtc);

			if (currentBar == null)
			{
				currentBarStartUtc = bucket;
				currentBar = new MutableBar(tick.Price);
				currentBar.AddTick(tick);
				return false;
			}

			if (bucket == currentBarStartUtc)
			{
				currentBar.AddTick(tick);
				return false;
			}

			completedBar = FinalizeCurrentBar();
			currentBarStartUtc = bucket;
			currentBar = new MutableBar(tick.Price);
			currentBar.AddTick(tick);
			return completedBar != null;
		}

		public bool TryFlush(out BarData completedBar)
		{
			completedBar = FinalizeCurrentBar();
			return completedBar != null;
		}

		private BarData FinalizeCurrentBar()
		{
			if (currentBar == null)
				return null;

			BarData bar = currentBar.ToBarData(currentBarStartUtc, tickSize, engineState);
			currentBar = null;
			if (bar != null && engineState != null)
				engineState.ApplyCompletedBar(bar);
			return bar;
		}

		private DateTime AlignTimestamp(DateTime timestampUtc)
		{
			long ticks = timestampUtc.Ticks - (timestampUtc.Ticks % barSize.Ticks);
			return new DateTime(ticks, DateTimeKind.Utc);
		}

		private sealed class MutableBar
		{
			private readonly SortedDictionary<double, OrderFlowPriceLevel> levels;

			public MutableBar(double startingPrice)
			{
				levels = new SortedDictionary<double, OrderFlowPriceLevel>();
				Open = startingPrice;
				High = startingPrice;
				Low = startingPrice;
				Close = startingPrice;
			}

			public double Open { get; private set; }
			public double High { get; private set; }
			public double Low { get; private set; }
			public double Close { get; private set; }
			public long Volume { get; private set; }

			public void AddTick(TickData tick)
			{
				if (tick.Price > High)
					High = tick.Price;
				if (tick.Price < Low)
					Low = tick.Price;

				Close = tick.Price;
				Volume += tick.Volume;

				OrderFlowPriceLevel level;
				if (!levels.TryGetValue(tick.Price, out level))
				{
					level = new OrderFlowPriceLevel { Price = tick.Price };
					levels[tick.Price] = level;
				}

				if (tick.IsBuyerInitiated)
					level.AskVolume += tick.Volume;
				else
					level.BidVolume += tick.Volume;
			}

			public BarData ToBarData(DateTime timestampUtc, double tickSize, EngineState state)
			{
				OrderFlowData orderFlow = BuildOrderFlowData(levels.Values);
				double averageVolume = state == null ? 0 : state.VolumeStats.Average;
				double averageRange = state == null ? 0 : state.RangeStats.Average;
				double priorSwingHigh = state == null || state.PriorSwingHigh == 0 ? High : state.PriorSwingHigh;
				double priorSwingLow = state == null || state.PriorSwingLow == 0 ? Low : state.PriorSwingLow;
				return new BarData
				{
					TimestampUtc = timestampUtc,
					Open = Open,
					High = High,
					Low = Low,
					Close = Close,
					Volume = Volume,
					AverageVolume = averageVolume,
					AverageRange = averageRange,
					PriorSwingHigh = priorSwingHigh,
					PriorSwingLow = priorSwingLow,
					PriorSignalDirection = state == null ? IntentDirection.Neutral : state.LastSignalDirection,
					PriorIntentScore = state == null ? 0 : state.LastIntentScore,
					TickSize = tickSize,
					OrderFlow = orderFlow
				};
			}

			private static OrderFlowData BuildOrderFlowData(IEnumerable<OrderFlowPriceLevel> levels)
			{
				OrderFlowData data = new OrderFlowData();
				double maxAskRatio = 0;
				double maxBidRatio = 0;
				OrderFlowPriceLevel highestPriceLevel = null;
				OrderFlowPriceLevel lowestPriceLevel = null;

				foreach (OrderFlowPriceLevel level in levels)
				{
					data.IsAvailable = true;
					data.TotalBuyingVolume += level.AskVolume;
					data.TotalSellingVolume += level.BidVolume;
					data.BarDelta += level.Delta;

					if (highestPriceLevel == null || level.Price > highestPriceLevel.Price)
						highestPriceLevel = level;
					if (lowestPriceLevel == null || level.Price < lowestPriceLevel.Price)
						lowestPriceLevel = level;

					double askRatio = SignalMath.SafeRatio(level.AskVolume, Math.Max(1, level.BidVolume));
					double bidRatio = SignalMath.SafeRatio(level.BidVolume, Math.Max(1, level.AskVolume));
					if (askRatio > maxAskRatio)
						maxAskRatio = askRatio;
					if (bidRatio > maxBidRatio)
						maxBidRatio = bidRatio;
					if (askRatio >= 2.5 && level.AskVolume >= 1)
						data.AskImbalanceLevels++;
					if (bidRatio >= 2.5 && level.BidVolume >= 1)
						data.BidImbalanceLevels++;
				}

				data.AskImbalanceRatio = maxAskRatio;
				data.BidImbalanceRatio = maxBidRatio;
				data.DeltaSh = highestPriceLevel == null ? 0 : highestPriceLevel.Delta;
				data.DeltaSl = lowestPriceLevel == null ? 0 : lowestPriceLevel.Delta;
				data.DeltaPerVolume = SignalMath.SafeRatio(Math.Abs(data.BarDelta), Math.Max(1, data.TotalBuyingVolume + data.TotalSellingVolume));
				List<OrderFlowPriceLevel> orderedLevels = new List<OrderFlowPriceLevel>(levels);
				orderedLevels.Sort((left, right) => left.Price.CompareTo(right.Price));
				data.PriceLevels = orderedLevels;
				return data;
			}
		}
	}
}
