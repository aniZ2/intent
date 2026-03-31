using Intent.Engine.Models;

namespace Intent.Engine.Ingestion
{
	public interface IBarBuilder
	{
		bool TryAddTick(TickData tick, out BarData completedBar);
		bool TryFlush(out BarData completedBar);
	}
}
