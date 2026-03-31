using System;

namespace Intent.Engine.State
{
	public sealed class SessionContext
	{
		public DateTime SessionDateUtc { get; set; }
		public double SessionHigh { get; set; }
		public double SessionLow { get; set; }
		public long SessionDelta { get; set; }
		public int BarsInSession { get; set; }

		public void Reset(DateTime sessionDateUtc, double high, double low)
		{
			SessionDateUtc = sessionDateUtc.Date;
			SessionHigh = high;
			SessionLow = low;
			SessionDelta = 0;
			BarsInSession = 0;
		}

		public void Update(double high, double low, long barDelta)
		{
			if (BarsInSession == 0)
			{
				SessionHigh = high;
				SessionLow = low;
			}
			else
			{
				if (high > SessionHigh)
					SessionHigh = high;
				if (low < SessionLow)
					SessionLow = low;
			}

			SessionDelta += barDelta;
			BarsInSession++;
		}
	}
}
