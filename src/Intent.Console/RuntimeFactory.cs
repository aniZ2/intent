using System;
using Intent.Engine.Ingestion;
using Intent.Engine.Models;
using Intent.Engine.Runtime;
using Intent.Engine.Signals;
using Intent.Engine.State;

namespace Intent.StreamRunner
{
	internal static class RuntimeFactory
	{
		public static IntentRuntime Create(RunnerOptions options)
		{
			EngineSettings settings = new EngineSettings();
			settings.SessionRolloverHourUtc = options.SessionRolloverHourUtc;
			EngineState state = new EngineState(options.VolumeLookback, options.RangeLookback, options.StructureLookback);
			state.SessionRolloverHourUtc = options.SessionRolloverHourUtc;
			IBarBuilder barBuilder = new BarBuilder(TimeSpan.FromSeconds(options.BarSeconds), state, options.TickSize);
			return new IntentRuntime(
				settings,
				new IntentSignalEngine(),
				state,
				barBuilder,
				options.EmitCompletedBars,
				options.EmitSignalEvents,
				options.DefaultInstrument);
		}
	}
}
