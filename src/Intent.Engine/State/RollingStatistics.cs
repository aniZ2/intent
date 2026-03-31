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

		public double Average
		{
			get { return values.Count == 0 ? 0 : sum / values.Count; }
		}
	}
}
