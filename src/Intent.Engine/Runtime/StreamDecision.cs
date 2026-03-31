using Intent.Engine.Models;
using Intent.Engine.Signals;

namespace Intent.Engine.Runtime
{
	public sealed class StreamDecision
	{
		public string EventType { get; set; }
		public BarData Bar { get; set; }
		public SignalResult Result { get; set; }
		public DecisionPacket Packet { get; set; }
	}
}
