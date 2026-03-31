using System.Collections.Generic;
using Intent.Engine.Models;

namespace Intent.Engine.Runtime
{
	public sealed class TickProcessingResult
	{
		public TickProcessingResult()
		{
			Emissions = new List<StreamDecision>();
		}

		public BarData CompletedBar { get; set; }
		public List<StreamDecision> Emissions { get; private set; }
	}
}
