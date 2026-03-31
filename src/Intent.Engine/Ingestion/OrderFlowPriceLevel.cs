namespace Intent.Engine.Ingestion
{
	public sealed class OrderFlowPriceLevel
	{
		public double Price { get; set; }
		public long AskVolume { get; set; }
		public long BidVolume { get; set; }

		public long Delta
		{
			get { return AskVolume - BidVolume; }
		}

		public long TotalVolume
		{
			get { return AskVolume + BidVolume; }
		}
	}
}
