using System.Collections.Generic;
using Intent.Engine.Ingestion;

namespace Intent.Engine.Models
{
	public sealed class OrderFlowData
	{
		public bool IsAvailable { get; set; }
		public long TotalBuyingVolume { get; set; }
		public long TotalSellingVolume { get; set; }
		public long BarDelta { get; set; }
		public long DeltaSh { get; set; }
		public long DeltaSl { get; set; }
		public int AskImbalanceLevels { get; set; }
		public int BidImbalanceLevels { get; set; }
		public double AskImbalanceRatio { get; set; }
		public double BidImbalanceRatio { get; set; }
		public double DeltaPerVolume { get; set; }
		public List<OrderFlowPriceLevel> PriceLevels { get; set; }
	}
}
