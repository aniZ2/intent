using System.Collections.Generic;

namespace Intent.Engine.State
{
	public sealed class RollingStatistics
	{
		private readonly Queue<double> values;
		private readonly int capacity;
		private double sum;

		public RollingStatistics(int capacity)
		{
			this.capacity = capacity < 1 ? 1 : capacity;
			values = new Queue<double>(this.capacity);
		}

		public void Add(double value)
		{
			values.Enqueue(value);
			sum += value;

			while (values.Count > capacity)
				sum -= values.Dequeue();
		}

		public bool IsReady
		{
			get { return values.Count >= capacity; }
		}

		public double Average
		{
			// Partial-window mean during warmup (ramps in gracefully) instead of returning 0,
			// which previously zeroed VolumeSpike/RangeExpansion for the first `capacity` bars
			// and silently disabled those factors on a fresh stream. Use IsReady to gate emission
			// when a full window is required.
			get { return values.Count == 0 ? 0 : sum / values.Count; }
		}
	}
}
