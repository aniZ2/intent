#region Using declarations
using Intent.Engine.Models;
using NinjaTrader.Data;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
	internal interface IIntentPlatformAdapter
	{
		EngineSettings BuildSettings();
		BarData BuildBarData(EngineSettings settings);
		TickData BuildTickData(MarketDataEventArgs marketDataUpdate);
	}
}
