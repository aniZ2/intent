using System.Collections.Generic;
using Intent.Engine.Models;
using Intent.Engine.Signals;

namespace Intent.Engine.State
{
	public sealed class EngineState
	{
		private readonly Queue<double> swingHighs;
		private readonly Queue<double> swingLows;
		private readonly int structureLookback;

		public EngineState(int volumeLookback, int rangeLookback, int structureLookback)
		{
			VolumeStats = new RollingStatistics(volumeLookback);
			RangeStats = new RollingStatistics(rangeLookback);
			this.structureLookback = structureLookback < 1 ? 1 : structureLookback;
			swingHighs = new Queue<double>(this.structureLookback);
			swingLows = new Queue<double>(this.structureLookback);
			Session = new SessionContext();
		}

		public RollingStatistics VolumeStats { get; private set; }
		public RollingStatistics RangeStats { get; private set; }
		public SessionContext Session { get; private set; }
		public IntentDirection LastSignalDirection { get; private set; }
		public double LastIntentScore { get; private set; }

		public double PriorSwingHigh
		{
			get
			{
				double highest = double.MinValue;
				foreach (double value in swingHighs)
					if (value > highest)
						highest = value;
				return highest == double.MinValue ? 0 : highest;
			}
		}

		public double PriorSwingLow
		{
			get
			{
				double lowest = double.MaxValue;
				foreach (double value in swingLows)
					if (value < lowest)
						lowest = value;
				return lowest == double.MaxValue ? 0 : lowest;
			}
		}

		public void ApplyCompletedBar(BarData bar)
		{
			VolumeStats.Add(bar.Volume);
			RangeStats.Add(bar.Range);

			swingHighs.Enqueue(bar.High);
			swingLows.Enqueue(bar.Low);

			while (swingHighs.Count > structureLookback)
				swingHighs.Dequeue();
			while (swingLows.Count > structureLookback)
				swingLows.Dequeue();

			if (Session.BarsInSession == 0 || Session.SessionDateUtc != bar.TimestampUtc.Date)
				Session.Reset(bar.TimestampUtc.Date, bar.High, bar.Low);

			Session.Update(bar.High, bar.Low, bar.OrderFlow != null ? bar.OrderFlow.BarDelta : 0);
		}

		public void ApplySignalResult(SignalResult result)
		{
			if (result == null)
				return;

			LastSignalDirection = result.Direction;
			LastIntentScore = result.IntentScore;
		}
	}
}
