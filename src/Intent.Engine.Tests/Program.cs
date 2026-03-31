using System;
using System.Collections.Generic;
using Intent.Engine.Ingestion;
using Intent.Engine.Models;
using Intent.Engine.Runtime;
using Intent.Engine.Signals;
using Intent.Engine.State;

namespace Intent.Engine.Tests
{
	internal static class Program
	{
		private static readonly EngineSettings Settings = new EngineSettings();
		private static readonly IntentSignalEngine Engine = new IntentSignalEngine();

		private static int Main()
		{
			try
			{
				TestAbsorptionDetection();
				TestImbalanceDetection();
				TestFailedBreakoutTrap();
				TestLiquiditySweep();
				TestNoTradeScenario();
				TestLowQualityBreakoutDoesNotTrigger();
				TestScoringConsistency();
				TestExplainability();
				TestStructuredDecisionPacket();
				TestOrderFlowOverridesWeakBarStructure();
				TestEngineStateSequenceBuildsTrapContext();
				TestEngineStateSequenceBuildsSweepContext();
				TestRuntimeHybridStreamingEmitsBarAndSignalPackets();
				Console.WriteLine("Behavior tests passed.");
				return 0;
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex.Message);
				return 1;
			}
		}

		private static void TestAbsorptionDetection()
		{
			BarData bar = CreateBaseBar();
			bar.Open = 100.40;
			bar.High = 100.50;
			bar.Low = 100.00;
			bar.Close = 100.44;
			bar.Volume = 1200;
			bar.AverageVolume = 900;
			bar.AverageRange = 0.75;
			bar.OrderFlow = CreateOrderFlow(
				260,
				940,
				1,
				4,
				2.1,
				4.8,
				CreateLevels(
					CreateLevel(100.00, 30, 250),
					CreateLevel(100.25, 45, 260),
					CreateLevel(100.50, 185, 40)));

			SignalResult result = Analyze(bar);

			AssertTrue("Absorption score should be elevated.", result.Absorption.Bullish >= 75);
			AssertTrue("Absorption should bias bullish at the detector level.", result.Absorption.Bullish > result.Absorption.Bearish);
			AssertReason("Absorption should explain itself.", result.Absorption.GetReason(IntentDirection.Bullish));
		}

		private static void TestImbalanceDetection()
		{
			BarData bar = CreateBaseBar();
			bar.Open = 100.00;
			bar.High = 101.00;
			bar.Low = 99.75;
			bar.Close = 100.95;
			bar.Volume = 1500;
			bar.AverageVolume = 900;
			bar.AverageRange = 0.90;
			bar.OrderFlow = CreateOrderFlow(
				1150,
				350,
				5,
				0,
				6.2,
				1.0,
				CreateLevels(
					CreateLevel(99.75, 80, 40),
					CreateLevel(100.00, 180, 45),
					CreateLevel(100.25, 220, 40),
					CreateLevel(100.50, 240, 35),
					CreateLevel(100.75, 260, 30),
					CreateLevel(101.00, 170, 25)));

			SignalResult result = Analyze(bar);

			AssertTrue("Imbalance score should be elevated.", result.Imbalance.Bullish >= 80);
			AssertTrue("Imbalance detector should bias bullish.", result.Imbalance.Bullish > result.Imbalance.Bearish);
			AssertTrue("Imbalance scenario should lean bullish overall.", result.BullScore > result.BearScore);
			AssertReason("Imbalance should explain itself.", result.Imbalance.GetReason(IntentDirection.Bullish));
		}

		private static void TestFailedBreakoutTrap()
		{
			BarData bar = CreateBaseBar();
			bar.Open = 102.40;
			bar.High = 102.50;
			bar.Low = 100.00;
			bar.Close = 100.25;
			bar.PriorSwingHigh = 101.00;
			bar.PriorSwingLow = 99.50;
			bar.Volume = 1800;
			bar.AverageVolume = 1100;
			bar.AverageRange = 1.00;
			bar.OrderFlow = CreateOrderFlow(
				500,
				1300,
				0,
				5,
				1.2,
				5.5,
				CreateLevels(
					CreateLevel(100.00, 40, 120),
					CreateLevel(100.50, 50, 200),
					CreateLevel(101.00, 60, 220),
					CreateLevel(101.50, 40, 260),
					CreateLevel(102.00, 35, 280),
					CreateLevel(102.50, 20, 220)));

			SignalResult result = Analyze(bar);

			AssertTrue("Failed breakout score should be elevated.", result.FailedBreakout.Bearish >= 65);
			AssertTrue("Failed breakout detector should bias bearish.", result.FailedBreakout.Bearish > result.FailedBreakout.Bullish);
			AssertTrue("Trap scenario should lean bearish overall.", result.BearScore > result.BullScore);
			AssertReason("Failed breakout should explain itself.", result.FailedBreakout.GetReason(IntentDirection.Bearish));
		}

		private static void TestLiquiditySweep()
		{
			BarData bar = CreateBaseBar();
			bar.Open = 100.50;
			bar.High = 101.00;
			bar.Low = 98.00;
			bar.Close = 100.75;
			bar.PriorSwingHigh = 101.50;
			bar.PriorSwingLow = 100.00;
			bar.Volume = 2100;
			bar.AverageVolume = 700;
			bar.AverageRange = 1.10;
			bar.OrderFlow = CreateOrderFlow(
				1500,
				600,
				5,
				1,
				5.0,
				1.7,
				CreateLevels(
					CreateLevel(98.00, 320, 30),
					CreateLevel(98.25, 280, 40),
					CreateLevel(98.50, 220, 50),
					CreateLevel(99.00, 180, 80),
					CreateLevel(100.00, 160, 130),
					CreateLevel(100.50, 170, 170),
					CreateLevel(101.00, 170, 100)));

			SignalResult result = Analyze(bar);

			AssertTrue("Liquidity sweep score should be elevated.", result.LiquiditySweep.Bullish >= 75);
			AssertTrue("Sweep detector should bias bullish.", result.LiquiditySweep.Bullish > result.LiquiditySweep.Bearish);
			AssertTrue("Sweep scenario should lean bullish overall.", result.BullScore > result.BearScore);
			AssertReason("Liquidity sweep should explain itself.", result.LiquiditySweep.GetReason(IntentDirection.Bullish));
		}

		private static void TestNoTradeScenario()
		{
			BarData bar = CreateBaseBar();
			bar.Open = 100.00;
			bar.High = 100.15;
			bar.Low = 99.95;
			bar.Close = 100.04;
			bar.Volume = 550;
			bar.AverageVolume = 800;
			bar.AverageRange = 0.90;
			bar.OrderFlow = CreateOrderFlow(
				275,
				275,
				1,
				1,
				1.1,
				1.1,
				CreateLevels(
					CreateLevel(99.95, 80, 75),
					CreateLevel(100.00, 95, 100),
					CreateLevel(100.05, 100, 100)));

			SignalResult result = Analyze(bar);

			AssertTrue("Low-volatility chop should stay below threshold.", result.IntentScore < Settings.SignalThreshold);
			AssertEqual("Low-volatility chop should remain neutral.", IntentDirection.Neutral, result.Direction);
		}

		private static void TestScoringConsistency()
		{
			BarData bar = CreateBaseBar();
			bar.Open = 100.00;
			bar.High = 101.00;
			bar.Low = 99.75;
			bar.Close = 100.95;
			bar.Volume = 1500;
			bar.AverageVolume = 900;
			bar.AverageRange = 0.90;
			bar.OrderFlow = CreateOrderFlow(
				1150,
				350,
				5,
				0,
				6.2,
				1.0,
				CreateLevels(
					CreateLevel(99.75, 80, 40),
					CreateLevel(100.00, 180, 45),
					CreateLevel(100.25, 220, 40),
					CreateLevel(100.50, 240, 35),
					CreateLevel(100.75, 260, 30),
					CreateLevel(101.00, 170, 25)));

			SignalResult first = Analyze(bar);
			SignalResult second = Analyze(bar);

			AssertEqual("Intent score should be deterministic.", first.IntentScore, second.IntentScore);
			AssertEqual("Bull score should be deterministic.", first.BullScore, second.BullScore);
			AssertEqual("Bear score should be deterministic.", first.BearScore, second.BearScore);
			AssertEqual("Direction should be deterministic.", first.Direction, second.Direction);
			AssertEqual("Reason should be deterministic.", first.DominantReason, second.DominantReason);
		}

		private static void TestLowQualityBreakoutDoesNotTrigger()
		{
			BarData bar = CreateBaseBar();
			bar.Open = 101.00;
			bar.High = 101.75;
			bar.Low = 100.90;
			bar.Close = 101.45;
			bar.PriorSwingHigh = 101.50;
			bar.PriorSwingLow = 100.50;
			bar.Volume = 650;
			bar.AverageVolume = 900;
			bar.AverageRange = 0.95;
			bar.OrderFlow = CreateOrderFlow(
				330,
				320,
				1,
				1,
				1.3,
				1.2,
				CreateLevels(
					CreateLevel(101.00, 80, 70),
					CreateLevel(101.25, 90, 85),
					CreateLevel(101.50, 85, 80),
					CreateLevel(101.75, 75, 85)));

			SignalResult result = Analyze(bar);

			AssertTrue("Weak breakout should not trigger failed-breakout confidence.", result.FailedBreakout.Bearish < 55);
			AssertTrue("Weak breakout should not trigger sweep confidence.", result.LiquiditySweep.Bearish < 55);
			AssertEqual("Weak breakout should remain neutral.", IntentDirection.Neutral, result.Direction);
		}

		private static void TestExplainability()
		{
			SignalResult imbalanceResult = Analyze(CreateHighConfidenceImbalanceBar());
			SignalResult failedBreakoutResult = Analyze(CreateHighConfidenceFailedBreakoutBar());
			SignalResult sweepResult = Analyze(CreateHighConfidenceSweepBar());

			AssertReason("High-confidence imbalance should expose a reason.", imbalanceResult.Imbalance.GetReason(IntentDirection.Bullish));
			AssertTrue("High-confidence imbalance should expose factor breakdown.", imbalanceResult.Imbalance.BullishFactors.Length > 0);

			AssertReason("High-confidence failed breakout should expose a reason.", failedBreakoutResult.FailedBreakout.GetReason(IntentDirection.Bearish));
			AssertTrue("High-confidence failed breakout should expose factor breakdown.", failedBreakoutResult.FailedBreakout.BearishFactors.Length > 0);

			AssertReason("High-confidence sweep should expose a reason.", sweepResult.LiquiditySweep.GetReason(IntentDirection.Bullish));
			AssertTrue("High-confidence sweep should expose factor breakdown.", sweepResult.LiquiditySweep.BullishFactors.Length > 0);

			if (imbalanceResult.IntentScore >= Settings.SignalThreshold)
				AssertReason("Composite imbalance result should expose a dominant reason.", imbalanceResult.DominantReason);
			if (failedBreakoutResult.IntentScore >= Settings.SignalThreshold)
				AssertReason("Composite failed breakout result should expose a dominant reason.", failedBreakoutResult.DominantReason);
			if (sweepResult.IntentScore >= Settings.SignalThreshold)
				AssertReason("Composite sweep result should expose a dominant reason.", sweepResult.DominantReason);
		}

		private static void TestStructuredDecisionPacket()
		{
			SignalResult result = Analyze(CreateHighConfidenceFailedBreakoutBar());
			DecisionPacket packet = result.ToDecisionPacket();
			string json = packet.ToJson();

			AssertReason("Decision packet should carry a direction.", packet.Direction);
			AssertReason("Decision packet should carry a confidence label.", packet.Confidence);
			AssertTrue("Decision packet should expose product-ready target zones.", packet.TargetZones.Length >= 2);
			AssertTrue("Decision packet should expose dominant factors.", packet.Factors.Length > 0);
			AssertTrue("Decision packet should include signals.", packet.Signals.Length == 4);
			AssertTrue("Decision packet JSON should be API-ready.", json.Contains("\"intentScore\"") && json.Contains("\"signals\""));
			AssertTrue("Decision packet JSON should include streaming metadata.", json.Contains("\"confidence\"") && json.Contains("\"targetZones\""));
			AssertTrue("Decision packet JSON should include reasons.", json.Contains("dominantReason"));
		}

		private static void TestOrderFlowOverridesWeakBarStructure()
		{
			BarData bar = CreateBaseBar();
			bar.Open = 100.75;
			bar.High = 101.00;
			bar.Low = 99.75;
			bar.Close = 100.05;
			bar.Volume = 1600;
			bar.AverageVolume = 900;
			bar.AverageRange = 0.90;
			bar.OrderFlow = CreateOrderFlow(
				1325,
				275,
				5,
				0,
				6.0,
				1.0,
				CreateLevels(
					CreateLevel(99.75, 180, 25),
					CreateLevel(100.00, 240, 30),
					CreateLevel(100.25, 260, 35),
					CreateLevel(100.50, 300, 40),
					CreateLevel(100.75, 215, 70),
					CreateLevel(101.00, 130, 75)));

			SignalResult result = Analyze(bar);

			AssertTrue("Order flow should outweigh weak bearish close for imbalance.", result.Imbalance.Bullish > result.Imbalance.Bearish);
			AssertTrue("Composite score should still lean bullish when order flow is dominant.", result.BullScore > result.BearScore);
		}

		private static void TestEngineStateSequenceBuildsTrapContext()
		{
			EngineState state = new EngineState(20, 14, 3);
			ApplySequenceState(state,
				CreateHistoryBar(new DateTime(2026, 3, 30, 14, 27, 0, DateTimeKind.Utc), 100.00, 100.50, 99.75, 100.25, 900),
				CreateHistoryBar(new DateTime(2026, 3, 30, 14, 28, 0, DateTimeKind.Utc), 100.25, 100.75, 100.00, 100.60, 950),
				CreateHistoryBar(new DateTime(2026, 3, 30, 14, 29, 0, DateTimeKind.Utc), 100.60, 101.00, 100.25, 100.85, 1000));

			BarData trapBar = CreateBaseBar();
			trapBar.TimestampUtc = new DateTime(2026, 3, 30, 14, 30, 0, DateTimeKind.Utc);
			trapBar.Open = 102.20;
			trapBar.High = 102.50;
			trapBar.Low = 100.10;
			trapBar.Close = 100.30;
			trapBar.Volume = 1800;
			trapBar.AverageVolume = state.VolumeStats.Average;
			trapBar.AverageRange = state.RangeStats.Average;
			trapBar.PriorSwingHigh = state.PriorSwingHigh;
			trapBar.PriorSwingLow = state.PriorSwingLow;
			trapBar.OrderFlow = CreateOrderFlow(
				520,
				1280,
				0,
				5,
				1.2,
				5.2,
				CreateLevels(
					CreateLevel(100.25, 40, 120),
					CreateLevel(100.50, 55, 180),
					CreateLevel(101.00, 65, 230),
					CreateLevel(101.50, 35, 250),
					CreateLevel(102.00, 25, 270),
					CreateLevel(102.50, 15, 230)));

			SignalResult result = Analyze(trapBar);

			AssertTrue("Stateful trap bar should use prior highs from EngineState.", trapBar.PriorSwingHigh >= 101.00 && trapBar.PriorSwingHigh < 101.01);
			AssertTrue("Stateful trap sequence should produce bearish failed-breakout confidence.", result.FailedBreakout.Bearish >= 65);
			AssertTrue("Stateful trap sequence should lean bearish overall.", result.BearScore > result.BullScore);
		}

		private static void TestEngineStateSequenceBuildsSweepContext()
		{
			EngineState state = new EngineState(20, 14, 3);
			IBarBuilder builder = new BarBuilder(TimeSpan.FromMinutes(1), state, 0.25);
			List<BarData> bars = BuildBarsFromTicks(builder, BuildSweepSequenceTicks());

			AssertTrue("Tick-driven sequence should emit multiple completed bars.", bars.Count >= 4);

			BarData finalBar = bars[bars.Count - 1];
			SignalResult result = Analyze(finalBar);

			AssertTrue("Tick-driven bar builder should inject order flow.", finalBar.OrderFlow != null && finalBar.OrderFlow.IsAvailable);
			AssertTrue("Tick-driven sweep sequence should build prior low context.", finalBar.PriorSwingLow > 0);
			AssertTrue("Tick-driven sweep should trigger bullish sweep confidence.", result.LiquiditySweep.Bullish > result.LiquiditySweep.Bearish);
			AssertTrue("Tick-driven sweep should lean bullish overall.", result.BullScore > result.BearScore);
		}

		private static void TestRuntimeHybridStreamingEmitsBarAndSignalPackets()
		{
			EngineState state = new EngineState(20, 14, 3);
			IntentRuntime runtime = new IntentRuntime(
				Settings,
				new IntentSignalEngine(),
				state,
				new BarBuilder(TimeSpan.FromMinutes(1), state, 0.25),
				true,
				true,
				"ES 06-26");

			List<StreamDecision> emissions = new List<StreamDecision>();
			foreach (TickData tick in BuildSweepSequenceTicks())
			{
				tick.Instrument = "ES 06-26";
				TickProcessingResult result = runtime.OnTick(tick);
				emissions.AddRange(result.Emissions);
			}

			emissions.AddRange(runtime.FlushPending().Emissions);

			AssertTrue("Hybrid runtime should emit completed-bar packets.", ContainsEvent(emissions, "barClose"));
			AssertTrue("Hybrid runtime should emit signal packets when threshold is met.", ContainsEvent(emissions, "signal"));
			AssertTrue("Hybrid runtime packets should carry instrument context.", ContainsInstrument(emissions, "ES 06-26"));
		}

		private static SignalResult Analyze(BarData bar)
		{
			return Engine.Analyze(bar, Settings);
		}

		private static bool ContainsEvent(IEnumerable<StreamDecision> emissions, string eventType)
		{
			foreach (StreamDecision emission in emissions)
				if (string.Equals(emission.EventType, eventType, StringComparison.Ordinal))
					return true;

			return false;
		}

		private static bool ContainsInstrument(IEnumerable<StreamDecision> emissions, string instrument)
		{
			foreach (StreamDecision emission in emissions)
				if (emission.Packet != null && string.Equals(emission.Packet.Instrument, instrument, StringComparison.Ordinal))
					return true;

			return false;
		}

		private static BarData CreateHighConfidenceAbsorptionBar()
		{
			BarData bar = CreateBaseBar();
			bar.Open = 100.40;
			bar.High = 100.50;
			bar.Low = 100.00;
			bar.Close = 100.44;
			bar.Volume = 1200;
			bar.AverageVolume = 900;
			bar.AverageRange = 0.75;
			bar.OrderFlow = CreateOrderFlow(
				260,
				940,
				1,
				4,
				2.1,
				4.8,
				CreateLevels(
					CreateLevel(100.00, 30, 250),
					CreateLevel(100.25, 45, 260),
					CreateLevel(100.50, 185, 40)));
			return bar;
		}

		private static BarData CreateHighConfidenceImbalanceBar()
		{
			BarData bar = CreateBaseBar();
			bar.Open = 100.00;
			bar.High = 101.00;
			bar.Low = 99.75;
			bar.Close = 100.95;
			bar.Volume = 1500;
			bar.AverageVolume = 900;
			bar.AverageRange = 0.90;
			bar.OrderFlow = CreateOrderFlow(
				1150,
				350,
				5,
				0,
				6.2,
				1.0,
				CreateLevels(
					CreateLevel(99.75, 80, 40),
					CreateLevel(100.00, 180, 45),
					CreateLevel(100.25, 220, 40),
					CreateLevel(100.50, 240, 35),
					CreateLevel(100.75, 260, 30),
					CreateLevel(101.00, 170, 25)));
			return bar;
		}

		private static BarData CreateHighConfidenceSweepBar()
		{
			BarData bar = CreateBaseBar();
			bar.Open = 100.50;
			bar.High = 101.00;
			bar.Low = 98.00;
			bar.Close = 100.75;
			bar.PriorSwingHigh = 101.50;
			bar.PriorSwingLow = 100.00;
			bar.Volume = 2100;
			bar.AverageVolume = 700;
			bar.AverageRange = 1.10;
			bar.OrderFlow = CreateOrderFlow(
				1500,
				600,
				5,
				1,
				5.0,
				1.7,
				CreateLevels(
					CreateLevel(98.00, 320, 30),
					CreateLevel(98.25, 280, 40),
					CreateLevel(98.50, 220, 50),
					CreateLevel(99.00, 180, 80),
					CreateLevel(100.00, 160, 130),
					CreateLevel(100.50, 170, 170),
					CreateLevel(101.00, 170, 100)));
			return bar;
		}

		private static BarData CreateHighConfidenceFailedBreakoutBar()
		{
			BarData bar = CreateBaseBar();
			bar.Open = 102.40;
			bar.High = 102.50;
			bar.Low = 100.00;
			bar.Close = 100.25;
			bar.PriorSwingHigh = 101.00;
			bar.PriorSwingLow = 99.50;
			bar.Volume = 1800;
			bar.AverageVolume = 1100;
			bar.AverageRange = 1.00;
			bar.OrderFlow = CreateOrderFlow(
				500,
				1300,
				0,
				5,
				1.2,
				5.5,
				CreateLevels(
					CreateLevel(100.00, 40, 120),
					CreateLevel(100.50, 50, 200),
					CreateLevel(101.00, 60, 220),
					CreateLevel(101.50, 40, 260),
					CreateLevel(102.00, 35, 280),
					CreateLevel(102.50, 20, 220)));
			return bar;
		}

		private static BarData CreateBaseBar()
		{
			return new BarData
			{
				TimestampUtc = new DateTime(2026, 3, 30, 14, 30, 0, DateTimeKind.Utc),
				Open = 100.00,
				High = 100.50,
				Low = 99.75,
				Close = 100.25,
				Volume = 1000,
				AverageVolume = 900,
				AverageRange = 0.80,
				PriorSwingHigh = 101.50,
				PriorSwingLow = 99.50,
				TickSize = 0.25,
				OrderFlow = new OrderFlowData()
			};
		}

		private static BarData CreateHistoryBar(DateTime timestampUtc, double open, double high, double low, double close, long volume)
		{
			return new BarData
			{
				TimestampUtc = timestampUtc,
				Open = open,
				High = high,
				Low = low,
				Close = close,
				Volume = volume,
				AverageVolume = volume,
				AverageRange = Math.Max(high - low, 0.25),
				PriorSwingHigh = high,
				PriorSwingLow = low,
				TickSize = 0.25,
				OrderFlow = new OrderFlowData { IsAvailable = false }
			};
		}

		private static OrderFlowData CreateOrderFlow(long totalBuyingVolume, long totalSellingVolume, int askImbalanceLevels, int bidImbalanceLevels, double askImbalanceRatio, double bidImbalanceRatio, List<OrderFlowPriceLevel> levels)
		{
			OrderFlowData data = new OrderFlowData();
			data.IsAvailable = true;
			data.TotalBuyingVolume = totalBuyingVolume;
			data.TotalSellingVolume = totalSellingVolume;
			data.BarDelta = totalBuyingVolume - totalSellingVolume;
			data.AskImbalanceLevels = askImbalanceLevels;
			data.BidImbalanceLevels = bidImbalanceLevels;
			data.AskImbalanceRatio = askImbalanceRatio;
			data.BidImbalanceRatio = bidImbalanceRatio;
			data.DeltaPerVolume = SignalMath.SafeRatio(Math.Abs(data.BarDelta), Math.Max(1, totalBuyingVolume + totalSellingVolume));
			data.PriceLevels = levels;
			data.DeltaSh = levels.Count == 0 ? 0 : levels[levels.Count - 1].Delta;
			data.DeltaSl = levels.Count == 0 ? 0 : levels[0].Delta;
			return data;
		}

		private static List<OrderFlowPriceLevel> CreateLevels(params OrderFlowPriceLevel[] levels)
		{
			return new List<OrderFlowPriceLevel>(levels);
		}

		private static OrderFlowPriceLevel CreateLevel(double price, long askVolume, long bidVolume)
		{
			return new OrderFlowPriceLevel
			{
				Price = price,
				AskVolume = askVolume,
				BidVolume = bidVolume
			};
		}

		private static void ApplySequenceState(EngineState state, params BarData[] bars)
		{
			for (int index = 0; index < bars.Length; index++)
				state.ApplyCompletedBar(bars[index]);
		}

		private static List<BarData> BuildBarsFromTicks(IBarBuilder builder, IEnumerable<TickData> ticks)
		{
			List<BarData> bars = new List<BarData>();
			foreach (TickData tick in ticks)
			{
				BarData completedBar;
				if (builder.TryAddTick(tick, out completedBar))
					bars.Add(completedBar);
			}

			BarData flushedBar;
			if (builder.TryFlush(out flushedBar))
				bars.Add(flushedBar);

			return bars;
		}

		private static IEnumerable<TickData> BuildSweepSequenceTicks()
		{
			DateTime start = new DateTime(2026, 3, 30, 14, 30, 0, DateTimeKind.Utc);

			yield return CreateTick(start.AddSeconds(0), 100.50, 100, true);
			yield return CreateTick(start.AddSeconds(20), 100.25, 110, false);
			yield return CreateTick(start.AddSeconds(40), 100.00, 120, false);

			yield return CreateTick(start.AddMinutes(1).AddSeconds(0), 100.00, 100, false);
			yield return CreateTick(start.AddMinutes(1).AddSeconds(20), 100.25, 105, true);
			yield return CreateTick(start.AddMinutes(1).AddSeconds(40), 100.50, 115, true);

			yield return CreateTick(start.AddMinutes(2).AddSeconds(0), 100.50, 95, true);
			yield return CreateTick(start.AddMinutes(2).AddSeconds(20), 100.25, 100, false);
			yield return CreateTick(start.AddMinutes(2).AddSeconds(40), 100.00, 110, false);

			yield return CreateTick(start.AddMinutes(3).AddSeconds(0), 99.75, 260, true);
			yield return CreateTick(start.AddMinutes(3).AddSeconds(10), 99.50, 280, true);
			yield return CreateTick(start.AddMinutes(3).AddSeconds(20), 99.25, 300, true);
			yield return CreateTick(start.AddMinutes(3).AddSeconds(30), 99.00, 340, true);
			yield return CreateTick(start.AddMinutes(3).AddSeconds(40), 99.75, 220, true);
			yield return CreateTick(start.AddMinutes(3).AddSeconds(50), 100.25, 210, true);
		}

		private static TickData CreateTick(DateTime timestampUtc, double price, long volume, bool buyerInitiated)
		{
			return new TickData
			{
				TimestampUtc = timestampUtc,
				Price = price,
				Volume = volume,
				Bid = price - 0.25,
				Ask = price + 0.25,
				IsBuyerInitiated = buyerInitiated
			};
		}

		private static void AssertReason(string message, string reason)
		{
			if (string.IsNullOrWhiteSpace(reason))
				throw new InvalidOperationException(message);
		}

		private static void AssertTrue(string message, bool condition)
		{
			if (!condition)
				throw new InvalidOperationException(message);
		}

		private static void AssertEqual(string message, double expected, double actual)
		{
			if (Math.Abs(expected - actual) > 0.000001)
				throw new InvalidOperationException(string.Format("{0} Expected {1:0.######}, got {2:0.######}.", message, expected, actual));
		}

		private static void AssertEqual(string message, string expected, string actual)
		{
			if (!string.Equals(expected, actual, StringComparison.Ordinal))
				throw new InvalidOperationException(string.Format("{0} Expected '{1}', got '{2}'.", message, expected, actual));
		}

		private static void AssertEqual(string message, IntentDirection expected, IntentDirection actual)
		{
			if (expected != actual)
				throw new InvalidOperationException(string.Format("{0} Expected {1}, got {2}.", message, expected, actual));
		}

		private static void AssertEqual(string message, IntentSignalType expected, IntentSignalType actual)
		{
			if (expected != actual)
				throw new InvalidOperationException(string.Format("{0} Expected {1}, got {2}.", message, expected, actual));
		}
	}
}
