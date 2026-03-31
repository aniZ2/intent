using System;
using System.Diagnostics;
using Intent.Engine.Ingestion;
using Intent.Engine.Models;
using Intent.Engine.Signals;
using Intent.Engine.State;

namespace Intent.Engine.Runtime
{
	public sealed class IntentRuntime
	{
		private readonly EngineSettings settings;
		private readonly IntentSignalEngine engine;
		private readonly EngineState state;
		private readonly IBarBuilder barBuilder;
		private readonly bool emitCompletedBars;
		private readonly bool emitSignalEvents;
		private readonly string defaultInstrument;
		private string lastInstrument;

		public IntentRuntime(
			EngineSettings settings,
			IntentSignalEngine engine,
			EngineState state,
			IBarBuilder barBuilder,
			bool emitCompletedBars,
			bool emitSignalEvents,
			string defaultInstrument)
		{
			this.settings = settings ?? new EngineSettings();
			this.engine = engine ?? new IntentSignalEngine();
			this.state = state ?? new EngineState(20, 14, 20);
			this.barBuilder = barBuilder ?? new BarBuilder(TimeSpan.FromMinutes(1), this.state, 0.25);
			this.emitCompletedBars = emitCompletedBars;
			this.emitSignalEvents = emitSignalEvents;
			this.defaultInstrument = defaultInstrument ?? string.Empty;
			lastInstrument = this.defaultInstrument;
		}

		public TickProcessingResult OnTick(TickData tick)
		{
			TickProcessingResult outcome = new TickProcessingResult();
			if (tick == null)
				return outcome;

			if (!string.IsNullOrWhiteSpace(tick.Instrument))
				lastInstrument = tick.Instrument;
			else if (!string.IsNullOrWhiteSpace(defaultInstrument))
				tick.Instrument = defaultInstrument;

			BarData completedBar;
			if (!barBuilder.TryAddTick(tick, out completedBar))
				return outcome;

			outcome.CompletedBar = completedBar;
			AnalyzeCompletedBar(completedBar, lastInstrument, outcome);
			return outcome;
		}

		public TickProcessingResult FlushPending()
		{
			TickProcessingResult outcome = new TickProcessingResult();
			BarData completedBar;
			if (!barBuilder.TryFlush(out completedBar))
				return outcome;

			outcome.CompletedBar = completedBar;
			AnalyzeCompletedBar(completedBar, lastInstrument, outcome);
			return outcome;
		}

		private void AnalyzeCompletedBar(BarData completedBar, string instrument, TickProcessingResult outcome)
		{
			Stopwatch stopwatch = Stopwatch.StartNew();
			SignalResult result = engine.Analyze(completedBar, settings);
			stopwatch.Stop();
			state.ApplySignalResult(result);

			if (emitCompletedBars)
				outcome.Emissions.Add(CreateEmission("barClose", instrument, completedBar, result, stopwatch.Elapsed.TotalMilliseconds));

			if (emitSignalEvents && result.IntentScore >= settings.SignalThreshold)
				outcome.Emissions.Add(CreateEmission("signal", instrument, completedBar, result, stopwatch.Elapsed.TotalMilliseconds));
		}

		private StreamDecision CreateEmission(string eventType, string instrument, BarData bar, SignalResult result, double latencyMs)
		{
			DecisionPacket packet = result.ToDecisionPacket();
			packet.EventType = eventType;
			packet.Instrument = instrument ?? string.Empty;
			packet.Session = state.Session == null ? bar.TimestampUtc.ToString("yyyy-MM-dd") : state.Session.SessionDateUtc.ToString("yyyy-MM-dd");
			packet.LatencyMs = latencyMs;
			packet.DataQuality = bar != null && bar.OrderFlow != null && bar.OrderFlow.IsAvailable ? "FULL_ORDER_FLOW" : "PRICE_ONLY";

			if (string.IsNullOrWhiteSpace(packet.Invalidation))
				packet.Invalidation = BuildInvalidation(bar, result.Direction);

			return new StreamDecision
			{
				EventType = eventType,
				Bar = bar,
				Result = result,
				Packet = packet
			};
		}

		private static string BuildInvalidation(BarData bar, IntentDirection direction)
		{
			if (bar == null)
				return string.Empty;

			if (direction == IntentDirection.Bullish)
				return string.Format(System.Globalization.CultureInfo.InvariantCulture, "Acceptance back below {0:0.#####}", bar.Low);
			if (direction == IntentDirection.Bearish)
				return string.Format(System.Globalization.CultureInfo.InvariantCulture, "Acceptance back above {0:0.#####}", bar.High);

			return string.Empty;
		}
	}
}
