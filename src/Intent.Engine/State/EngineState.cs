using System;
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

		// UTC hour (0-23) at which a new trading session begins. 0 (default) preserves UTC-midnight
		// rollover. For CME-style futures the calendar day boundary (00:00 UTC) falls mid-session, so
		// set this to the exchange session-open hour in UTC (e.g. 22 for ~17:00 ET) so session
		// high/low/cumulative-delta reset on the real session boundary rather than mid-afternoon.
		public int SessionRolloverHourUtc { get; set; }

		private DateTime SessionDateFor(DateTime timestampUtc)
		{
			int hour = SessionRolloverHourUtc;
			if (hour <= 0 || hour > 23)
				return timestampUtc.Date;
			return timestampUtc.AddHours(24 - hour).Date;
		}

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

			DateTime sessionDate = SessionDateFor(bar.TimestampUtc);
			if (Session.BarsInSession == 0 || Session.SessionDateUtc != sessionDate)
				Session.Reset(sessionDate, bar.High, bar.Low);

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
