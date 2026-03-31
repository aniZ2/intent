using System;

namespace Intent.Engine.Models
{
	public sealed class TickData
	{
		public DateTime TimestampUtc { get; set; }
		public string Instrument { get; set; }
		public double Price { get; set; }
		public long Volume { get; set; }
		public double Bid { get; set; }
		public double Ask { get; set; }
		public bool IsBuyerInitiated { get; set; }
	}
}
