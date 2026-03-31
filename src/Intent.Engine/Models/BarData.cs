using System;
using Intent.Engine.Signals;

namespace Intent.Engine.Models
{
	public sealed class BarData
	{
		public BarData()
		{
			OrderFlow = new OrderFlowData();
		}

		public DateTime TimestampUtc { get; set; }
		public double Open { get; set; }
		public double High { get; set; }
		public double Low { get; set; }
		public double Close { get; set; }
		public long Volume { get; set; }
		public double AverageVolume { get; set; }
		public double AverageRange { get; set; }
		public double PriorSwingHigh { get; set; }
		public double PriorSwingLow { get; set; }
		public IntentDirection PriorSignalDirection { get; set; }
		public double PriorIntentScore { get; set; }
		public double TickSize { get; set; }
		public OrderFlowData OrderFlow { get; set; }

		public double Range
		{
			get { return Math.Max(High - Low, TickSize <= 0 ? 0.0000001 : TickSize); }
		}

		public double Body
		{
			get { return Close - Open; }
		}

		public double BodyRatio
		{
			get { return Math.Abs(Body) / Range; }
		}

		public double UpperWick
		{
			get { return Math.Max(High - Math.Max(Open, Close), 0); }
		}

		public double LowerWick
		{
			get { return Math.Max(Math.Min(Open, Close) - Low, 0); }
		}

		public double UpperWickRatio
		{
			get { return UpperWick / Range; }
		}

		public double LowerWickRatio
		{
			get { return LowerWick / Range; }
		}

		public double CloseLocation
		{
			get { return (Close - Low) / Range; }
		}

		public double VolumeSpike
		{
			get { return AverageVolume <= 0 ? 0 : Volume / AverageVolume; }
		}

		public double RangeExpansion
		{
			get { return AverageRange <= 0 ? 0 : Range / AverageRange; }
		}

		public double BreakAboveDistance
		{
			get { return Math.Max(0, High - PriorSwingHigh); }
		}

		public double BreakBelowDistance
		{
			get { return Math.Max(0, PriorSwingLow - Low); }
		}

		public double ReclaimBelowHigh
		{
			get { return Math.Max(0, PriorSwingHigh - Close); }
		}

		public double ReclaimAboveLow
		{
			get { return Math.Max(0, Close - PriorSwingLow); }
		}

		public double BreakAboveTicks
		{
			get { return BreakAboveDistance / SafeTickSize; }
		}

		public double BreakBelowTicks
		{
			get { return BreakBelowDistance / SafeTickSize; }
		}

		public double ReclaimBelowHighTicks
		{
			get { return ReclaimBelowHigh / SafeTickSize; }
		}

		public double ReclaimAboveLowTicks
		{
			get { return ReclaimAboveLow / SafeTickSize; }
		}

		public double PriceEfficiency
		{
			get { return Math.Abs(Body) / Range; }
		}

		public bool IsBullishBody
		{
			get { return Body > 0; }
		}

		public bool IsBearishBody
		{
			get { return Body < 0; }
		}

		private double SafeTickSize
		{
			get { return TickSize <= 0 ? 0.0000001 : TickSize; }
		}
	}
}
