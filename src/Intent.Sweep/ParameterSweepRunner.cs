using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Intent.Engine.Ingestion;
using Intent.Engine.Models;
using Intent.Engine.Runtime;
using Intent.Engine.Signals;
using Intent.Engine.State;

namespace Intent.Sweep
{
	internal sealed class ParameterSweepRunner
	{
		private readonly SweepOptions options;

		public ParameterSweepRunner(SweepOptions options)
		{
			this.options = options;
		}

		public void Run()
		{
			List<TickSession> sessions = TickFileReader.ReadSessions(options.InputPath);
			if (sessions.Count == 0)
				throw new InvalidOperationException("No sessions were parsed from the input path.");

			List<ConfigSpec> configs = BuildConfigs();
			StreamWriter writer = null;

			try
			{
				if (!string.IsNullOrWhiteSpace(options.OutputPath))
				{
					string fullPath = Path.GetFullPath(options.OutputPath);
					string directory = Path.GetDirectoryName(fullPath);
					if (!string.IsNullOrWhiteSpace(directory))
						Directory.CreateDirectory(directory);
					writer = new StreamWriter(fullPath, false);
				}

				List<SweepSummary> summaries = sessions.Count >= options.TrainWindowSessions + 1
					? RunWalkForward(sessions, configs)
					: RunDirectEvaluation(sessions, configs);

				for (int index = 0; index < summaries.Count; index++)
					WriteRecord(summaries[index].ToJson(), writer);

				WriteRecord(BuildRankingJson(summaries, "accuracy"), writer);
			}
			finally
			{
				if (writer != null)
					writer.Dispose();
			}
		}

		private List<SweepSummary> RunWalkForward(List<TickSession> sessions, List<ConfigSpec> configs)
		{
			List<SweepSummary> summaries = new List<SweepSummary>();
			List<SweepSummary> bestTestSummaries = new List<SweepSummary>();

			for (int foldStart = 0; foldStart + options.TrainWindowSessions < sessions.Count; foldStart++)
			{
				List<TickSession> trainSessions = sessions.GetRange(foldStart, options.TrainWindowSessions);
				TickSession testSession = sessions[foldStart + options.TrainWindowSessions];

				SweepSummary bestTrainSummary = null;
				ConfigSpec bestConfig = null;

				for (int configIndex = 0; configIndex < configs.Count; configIndex++)
				{
					SweepSummary trainSummary = EvaluateConfig(trainSessions, configs[configIndex], "walkForwardTrain");
					summaries.Add(trainSummary);

					if (bestTrainSummary == null || CompareSummaries(trainSummary, bestTrainSummary) < 0)
					{
						bestTrainSummary = trainSummary;
						bestConfig = configs[configIndex];
					}
				}

				SweepSummary testSummary = EvaluateConfig(new List<TickSession> { testSession }, bestConfig, "walkForwardTest");
				testSummary.RecordType = "walkForwardTest";
				testSummary.FoldCount = 1;
				testSummary.FoldF1Scores.Add(testSummary.F1);
				summaries.Add(testSummary);
				bestTestSummaries.Add(testSummary);
			}

			SweepSummary defaultSummary = EvaluateConfig(sessions, BuildDefaultConfig(), "defaultComparison");
			summaries.Add(defaultSummary);
			return summaries;
		}

		private List<SweepSummary> RunDirectEvaluation(List<TickSession> sessions, List<ConfigSpec> configs)
		{
			List<SweepSummary> summaries = new List<SweepSummary>();
			for (int index = 0; index < configs.Count; index++)
				summaries.Add(EvaluateConfig(sessions, configs[index], "configSummary"));

			summaries.Add(EvaluateConfig(sessions, BuildDefaultConfig(), "defaultComparison"));
			return summaries;
		}

		private SweepSummary EvaluateConfig(List<TickSession> sessions, ConfigSpec config, string recordType)
		{
			SweepSummary summary = CreateSummary(config, recordType);
			List<double> sessionF1s = new List<double>();

			double scoreTotal = 0;
			double latencyTotal = 0;
			int latencyBars = 0;
			int timeToMoveWins = 0;
			double timeToMoveTotal = 0;
			double adverseExcursionResolved = 0;
			int resolvedSignals = 0;

			double fullTimeToMoveTotal = 0;
			int fullTimeToMoveWins = 0;
			double fullAdverseExcursionResolved = 0;
			int fullResolvedSignals = 0;

			double priceTimeToMoveTotal = 0;
			int priceTimeToMoveWins = 0;
			double priceAdverseExcursionResolved = 0;
			int priceResolvedSignals = 0;

			for (int sessionIndex = 0; sessionIndex < sessions.Count; sessionIndex++)
			{
				SessionMetrics metrics = EvaluateSession(sessions[sessionIndex], config);
				Accumulate(summary, metrics, ref scoreTotal, ref latencyTotal, ref latencyBars, ref timeToMoveTotal, ref timeToMoveWins, ref adverseExcursionResolved, ref resolvedSignals, ref fullTimeToMoveTotal, ref fullTimeToMoveWins, ref fullAdverseExcursionResolved, ref fullResolvedSignals, ref priceTimeToMoveTotal, ref priceTimeToMoveWins, ref priceAdverseExcursionResolved, ref priceResolvedSignals);
				sessionF1s.Add(metrics.F1);
			}

			summary.AverageScore = summary.CompletedBars == 0 ? 0 : scoreTotal / summary.CompletedBars;
			summary.AverageLatencyMs = latencyBars == 0 ? 0 : latencyTotal / latencyBars;
			summary.HitRateProxy = summary.SignalEvents == 0 ? 0 : (double)summary.WinningSignals / summary.SignalEvents;
			summary.SignalEfficiency = summary.HitRateProxy;
			summary.Precision = ComputePrecision(summary.WinningSignals, summary.FalsePositives);
			summary.Recall = ComputeRecall(summary.WinningSignals, summary.MissedSignals);
			summary.F1 = ComputeF1(summary.Precision, summary.Recall);
			summary.AverageTimeToMoveBars = timeToMoveWins == 0 ? 0 : timeToMoveTotal / timeToMoveWins;
			summary.AverageAdverseExcursionTicks = resolvedSignals == 0 ? 0 : adverseExcursionResolved / resolvedSignals;
			summary.StabilityPenalty = ComputeStandardDeviation(sessionF1s) * 2.0;
			summary.FinalScore = summary.F1 - summary.StabilityPenalty;
			summary.FoldCount = sessionF1s.Count;
			summary.FoldF1Scores.AddRange(sessionF1s);

			FinalizeQuality(summary.FullOrderFlow, fullTimeToMoveWins, fullTimeToMoveTotal, fullResolvedSignals, fullAdverseExcursionResolved);
			FinalizeQuality(summary.PriceOnly, priceTimeToMoveWins, priceTimeToMoveTotal, priceResolvedSignals, priceAdverseExcursionResolved);

			return summary;
		}

		private SessionMetrics EvaluateSession(TickSession session, ConfigSpec config)
		{
			EngineSettings settings = config.ToSettings();
			EngineState state = new EngineState(20, 14, 20);
			IntentRuntime runtime = new IntentRuntime(
				settings,
				new IntentSignalEngine(),
				state,
				new BarBuilder(TimeSpan.FromSeconds(options.BarSeconds), state, options.TickSize),
				true,
				true,
				string.Empty);

			List<BarData> completedBars = new List<BarData>();
			List<SignalCandidate> signalCandidates = new List<SignalCandidate>();
			SessionMetrics metrics = new SessionMetrics();

			for (int tickIndex = 0; tickIndex < session.Ticks.Count; tickIndex++)
				Consume(runtime.OnTick(CloneTick(session.Ticks[tickIndex])), metrics, completedBars, signalCandidates);

			Consume(runtime.FlushPending(), metrics, completedBars, signalCandidates);
			EvaluateSignalsAndMisses(metrics, completedBars, signalCandidates);
			metrics.Precision = ComputePrecision(metrics.WinningSignals, metrics.FalsePositives);
			metrics.Recall = ComputeRecall(metrics.WinningSignals, metrics.MissedSignals);
			metrics.F1 = ComputeF1(metrics.Precision, metrics.Recall);
			return metrics;
		}

		private void Consume(TickProcessingResult result, SessionMetrics metrics, List<BarData> completedBars, List<SignalCandidate> signalCandidates)
		{
			if (result == null)
				return;

			for (int index = 0; index < result.Emissions.Count; index++)
			{
				StreamDecision emission = result.Emissions[index];
				if (emission.Packet == null)
					continue;

				if (string.Equals(emission.EventType, "barClose", StringComparison.Ordinal))
				{
					metrics.CompletedBars++;
					metrics.ScoreTotal += emission.Packet.Score;
					metrics.LatencyTotal += emission.Packet.LatencyMs;
					metrics.LatencyBars++;
					if (emission.Packet.LatencyMs > metrics.MaxLatencyMs)
						metrics.MaxLatencyMs = emission.Packet.LatencyMs;
					if (emission.Bar != null)
						completedBars.Add(emission.Bar);
					if (string.Equals(emission.Packet.Direction, "Neutral", StringComparison.Ordinal))
						metrics.NeutralBars++;

					if (string.Equals(emission.Packet.DataQuality, "FULL_ORDER_FLOW", StringComparison.Ordinal))
						metrics.FullOrderFlowPackets++;
					else
						metrics.PriceOnlyPackets++;
				}
				else if (string.Equals(emission.EventType, "signal", StringComparison.Ordinal))
				{
					metrics.SignalEvents++;
					if (string.Equals(emission.Packet.Direction, "Bullish", StringComparison.Ordinal))
						metrics.BullishSignals++;
					else if (string.Equals(emission.Packet.Direction, "Bearish", StringComparison.Ordinal))
						metrics.BearishSignals++;

					if (signalCandidates.Count > 0 && !string.Equals(signalCandidates[signalCandidates.Count - 1].Direction, emission.Packet.Direction, StringComparison.Ordinal))
						metrics.DirectionFlips++;

					signalCandidates.Add(new SignalCandidate
					{
						BarIndex = Math.Max(0, completedBars.Count - 1),
						Direction = emission.Packet.Direction,
						EntryPrice = emission.Bar == null ? 0 : emission.Bar.Close,
						DataQuality = emission.Packet.DataQuality
					});
				}
			}
		}

		private void EvaluateSignalsAndMisses(SessionMetrics metrics, List<BarData> bars, List<SignalCandidate> signals)
		{
			HashSet<int> signaledBars = new HashSet<int>();
			for (int signalIndex = 0; signalIndex < signals.Count; signalIndex++)
			{
				SignalEvaluation evaluation = EvaluateSignal(signals[signalIndex], bars);
				signaledBars.Add(signals[signalIndex].BarIndex);

				QualityMetrics bucket = string.Equals(signals[signalIndex].DataQuality, "FULL_ORDER_FLOW", StringComparison.Ordinal)
					? metrics.FullOrderFlow
					: metrics.PriceOnly;
				bucket.SignalEvents++;

				if (evaluation.IsWin)
				{
					metrics.WinningSignals++;
					metrics.TimeToMoveTotal += evaluation.TimeToMoveBars;
					metrics.TimeToMoveWins++;
					metrics.AdverseExcursionResolved += evaluation.AdverseExcursionTicks;
					metrics.ResolvedSignals++;

					bucket.WinningSignals++;
					bucket.TimeToMoveTotal += evaluation.TimeToMoveBars;
					bucket.TimeToMoveWins++;
					bucket.AdverseExcursionResolved += evaluation.AdverseExcursionTicks;
					bucket.ResolvedSignals++;
				}
				else if (evaluation.IsLoss)
				{
					metrics.FalsePositives++;
					metrics.AdverseExcursionResolved += evaluation.AdverseExcursionTicks;
					metrics.ResolvedSignals++;

					bucket.FalsePositives++;
					bucket.AdverseExcursionResolved += evaluation.AdverseExcursionTicks;
					bucket.ResolvedSignals++;
				}
				else
				{
					metrics.UnresolvedSignals++;
					bucket.UnresolvedSignals++;
				}
			}

			for (int barIndex = 0; barIndex < bars.Count; barIndex++)
			{
				if (signaledBars.Contains(barIndex))
					continue;

				MissedOpportunity missed = EvaluateMissedOpportunity(barIndex, bars);
				if (missed.Direction == IntentDirection.Neutral)
					continue;

				metrics.MissedSignals++;
				if (missed.Direction == IntentDirection.Bullish || missed.Direction == IntentDirection.Bearish)
				{
					QualityMetrics bucket = bars[barIndex].OrderFlow != null && bars[barIndex].OrderFlow.IsAvailable
						? metrics.FullOrderFlow
						: metrics.PriceOnly;
					bucket.MissedSignals++;
				}
			}
		}

		private SignalEvaluation EvaluateSignal(SignalCandidate signal, List<BarData> bars)
		{
			SignalEvaluation evaluation = new SignalEvaluation();
			if (signal == null || signal.BarIndex < 0 || signal.BarIndex >= bars.Count)
				return evaluation;

			double tickSize = Math.Max(options.TickSize, 0.0000001);
			double entry = signal.EntryPrice;
			double target = signal.Direction == "Bullish" ? entry + (options.TargetTicks * tickSize) : entry - (options.TargetTicks * tickSize);
			double invalidation = signal.Direction == "Bullish" ? entry - (options.InvalidationTicks * tickSize) : entry + (options.InvalidationTicks * tickSize);
			double maxAdverseTicks = 0;
			int lastBarIndex = Math.Min(bars.Count - 1, signal.BarIndex + options.LookaheadBars);

			for (int barIndex = signal.BarIndex + 1; barIndex <= lastBarIndex; barIndex++)
			{
				BarData bar = bars[barIndex];
				double adverseTicks = signal.Direction == "Bullish"
					? Math.Max(0, (entry - bar.Low) / tickSize)
					: Math.Max(0, (bar.High - entry) / tickSize);
				if (adverseTicks > maxAdverseTicks)
					maxAdverseTicks = adverseTicks;

				bool targetTouched = signal.Direction == "Bullish" ? bar.High >= target : bar.Low <= target;
				bool invalidationTouched = signal.Direction == "Bullish" ? bar.Low <= invalidation : bar.High >= invalidation;

				if (targetTouched && invalidationTouched)
				{
					evaluation.IsLoss = true;
					evaluation.AdverseExcursionTicks = maxAdverseTicks;
					return evaluation;
				}

				if (targetTouched)
				{
					evaluation.IsWin = true;
					evaluation.TimeToMoveBars = barIndex - signal.BarIndex;
					evaluation.AdverseExcursionTicks = maxAdverseTicks;
					return evaluation;
				}

				if (invalidationTouched)
				{
					evaluation.IsLoss = true;
					evaluation.AdverseExcursionTicks = maxAdverseTicks;
					return evaluation;
				}
			}

			evaluation.AdverseExcursionTicks = maxAdverseTicks;
			return evaluation;
		}

		private MissedOpportunity EvaluateMissedOpportunity(int barIndex, List<BarData> bars)
		{
			MissedOpportunity bullish = EvaluateOpportunity(barIndex, bars, IntentDirection.Bullish);
			MissedOpportunity bearish = EvaluateOpportunity(barIndex, bars, IntentDirection.Bearish);

			if (bullish.Direction != IntentDirection.Neutral && bearish.Direction != IntentDirection.Neutral)
				return new MissedOpportunity();
			if (bullish.Direction != IntentDirection.Neutral)
				return bullish;
			return bearish;
		}

		private MissedOpportunity EvaluateOpportunity(int barIndex, List<BarData> bars, IntentDirection direction)
		{
			MissedOpportunity opportunity = new MissedOpportunity();
			if (barIndex < 0 || barIndex >= bars.Count)
				return opportunity;

			double tickSize = Math.Max(options.TickSize, 0.0000001);
			double entry = bars[barIndex].Close;
			double target = direction == IntentDirection.Bullish ? entry + (options.TargetTicks * tickSize) : entry - (options.TargetTicks * tickSize);
			double invalidation = direction == IntentDirection.Bullish ? entry - (options.InvalidationTicks * tickSize) : entry + (options.InvalidationTicks * tickSize);
			int lastBarIndex = Math.Min(bars.Count - 1, barIndex + options.LookaheadBars);

			for (int futureIndex = barIndex + 1; futureIndex <= lastBarIndex; futureIndex++)
			{
				BarData futureBar = bars[futureIndex];
				bool targetTouched = direction == IntentDirection.Bullish ? futureBar.High >= target : futureBar.Low <= target;
				bool invalidationTouched = direction == IntentDirection.Bullish ? futureBar.Low <= invalidation : futureBar.High >= invalidation;

				if (targetTouched && invalidationTouched)
					return opportunity;
				if (targetTouched)
				{
					opportunity.Direction = direction;
					return opportunity;
				}
				if (invalidationTouched)
					return opportunity;
			}

			return opportunity;
		}

		private void Accumulate(SweepSummary summary, SessionMetrics metrics, ref double scoreTotal, ref double latencyTotal, ref int latencyBars, ref double timeToMoveTotal, ref int timeToMoveWins, ref double adverseExcursionResolved, ref int resolvedSignals, ref double fullTimeToMoveTotal, ref int fullTimeToMoveWins, ref double fullAdverseExcursionResolved, ref int fullResolvedSignals, ref double priceTimeToMoveTotal, ref int priceTimeToMoveWins, ref double priceAdverseExcursionResolved, ref int priceResolvedSignals)
		{
			summary.CompletedBars += metrics.CompletedBars;
			summary.SignalEvents += metrics.SignalEvents;
			summary.MissedSignals += metrics.MissedSignals;
			summary.WinningSignals += metrics.WinningSignals;
			summary.FalsePositives += metrics.FalsePositives;
			summary.UnresolvedSignals += metrics.UnresolvedSignals;
			summary.BullishSignals += metrics.BullishSignals;
			summary.BearishSignals += metrics.BearishSignals;
			summary.NeutralBars += metrics.NeutralBars;
			summary.DirectionFlips += metrics.DirectionFlips;
			summary.FullOrderFlowPackets += metrics.FullOrderFlowPackets;
			summary.PriceOnlyPackets += metrics.PriceOnlyPackets;

			scoreTotal += metrics.ScoreTotal;
			latencyTotal += metrics.LatencyTotal;
			latencyBars += metrics.LatencyBars;
			if (metrics.MaxLatencyMs > summary.MaxLatencyMs)
				summary.MaxLatencyMs = metrics.MaxLatencyMs;

			timeToMoveTotal += metrics.TimeToMoveTotal;
			timeToMoveWins += metrics.TimeToMoveWins;
			adverseExcursionResolved += metrics.AdverseExcursionResolved;
			resolvedSignals += metrics.ResolvedSignals;

			summary.FullOrderFlow.SignalEvents += metrics.FullOrderFlow.SignalEvents;
			summary.FullOrderFlow.WinningSignals += metrics.FullOrderFlow.WinningSignals;
			summary.FullOrderFlow.FalsePositives += metrics.FullOrderFlow.FalsePositives;
			summary.FullOrderFlow.MissedSignals += metrics.FullOrderFlow.MissedSignals;
			summary.FullOrderFlow.UnresolvedSignals += metrics.FullOrderFlow.UnresolvedSignals;
			fullTimeToMoveTotal += metrics.FullOrderFlow.TimeToMoveTotal;
			fullTimeToMoveWins += metrics.FullOrderFlow.TimeToMoveWins;
			fullAdverseExcursionResolved += metrics.FullOrderFlow.AdverseExcursionResolved;
			fullResolvedSignals += metrics.FullOrderFlow.ResolvedSignals;

			summary.PriceOnly.SignalEvents += metrics.PriceOnly.SignalEvents;
			summary.PriceOnly.WinningSignals += metrics.PriceOnly.WinningSignals;
			summary.PriceOnly.FalsePositives += metrics.PriceOnly.FalsePositives;
			summary.PriceOnly.MissedSignals += metrics.PriceOnly.MissedSignals;
			summary.PriceOnly.UnresolvedSignals += metrics.PriceOnly.UnresolvedSignals;
			priceTimeToMoveTotal += metrics.PriceOnly.TimeToMoveTotal;
			priceTimeToMoveWins += metrics.PriceOnly.TimeToMoveWins;
			priceAdverseExcursionResolved += metrics.PriceOnly.AdverseExcursionResolved;
			priceResolvedSignals += metrics.PriceOnly.ResolvedSignals;
		}

		private static void FinalizeQuality(QualityBreakdown breakdown, int timeToMoveWins, double timeToMoveTotal, int resolvedSignals, double adverseExcursionResolved)
		{
			breakdown.HitRateProxy = breakdown.SignalEvents == 0 ? 0 : (double)breakdown.WinningSignals / breakdown.SignalEvents;
			breakdown.SignalEfficiency = breakdown.HitRateProxy;
			breakdown.Precision = ComputePrecision(breakdown.WinningSignals, breakdown.FalsePositives);
			breakdown.Recall = ComputeRecall(breakdown.WinningSignals, breakdown.MissedSignals);
			breakdown.F1 = ComputeF1(breakdown.Precision, breakdown.Recall);
			breakdown.AverageTimeToMoveBars = timeToMoveWins == 0 ? 0 : timeToMoveTotal / timeToMoveWins;
			breakdown.AverageAdverseExcursionTicks = resolvedSignals == 0 ? 0 : adverseExcursionResolved / resolvedSignals;
		}

		private static double ComputePrecision(int wins, int falsePositives)
		{
			int denominator = wins + falsePositives;
			return denominator == 0 ? 0 : (double)wins / denominator;
		}

		private static double ComputeRecall(int wins, int missedSignals)
		{
			int denominator = wins + missedSignals;
			return denominator == 0 ? 0 : (double)wins / denominator;
		}

		private static double ComputeF1(double precision, double recall)
		{
			double denominator = precision + recall;
			return denominator <= 0 ? 0 : (2.0 * precision * recall) / denominator;
		}

		private static double ComputeStandardDeviation(List<double> values)
		{
			if (values == null || values.Count == 0)
				return 0;

			double mean = 0;
			for (int index = 0; index < values.Count; index++)
				mean += values[index];
			mean /= values.Count;

			double variance = 0;
			for (int index = 0; index < values.Count; index++)
			{
				double delta = values[index] - mean;
				variance += delta * delta;
			}

			return Math.Sqrt(variance / values.Count);
		}

		private List<ConfigSpec> BuildConfigs()
		{
			List<ConfigSpec> configs = new List<ConfigSpec>();
			EngineSettings defaults = new EngineSettings();
			if (options.Mode == SweepMode.Imbalance)
			{
				foreach (double imbalanceRatioThreshold in options.ImbalanceRatioThresholds)
				foreach (double imbalanceLevelNormalizationSpan in options.ImbalanceLevelNormalizationSpans)
				foreach (double deltaPerVolumeBaseline in options.DeltaPerVolumeBaselines)
				foreach (double deltaPerVolumeNormalizationSpan in options.DeltaPerVolumeNormalizationSpans)
				foreach (double imbalanceVolumeSpikeThreshold in options.ImbalanceVolumeSpikeThresholds)
				foreach (long minImbalanceVolumePerLevel in options.MinImbalanceVolumePerLevels)
				{
					configs.Add(CreateDefaultConfig(defaults, defaults.SignalThreshold, defaults.ImbalanceWeight, defaults.AbsorptionWeight, defaults.ConfluenceBonus, defaults.NeutralityBuffer));
					ConfigSpec config = configs[configs.Count - 1];
					config.ImbalanceRatioThreshold = imbalanceRatioThreshold;
					config.ImbalanceLevelNormalizationSpan = imbalanceLevelNormalizationSpan;
					config.DeltaPerVolumeBaseline = deltaPerVolumeBaseline;
					config.DeltaPerVolumeNormalizationSpan = deltaPerVolumeNormalizationSpan;
					config.ImbalanceVolumeSpikeThreshold = imbalanceVolumeSpikeThreshold;
					config.MinImbalanceVolumePerLevel = minImbalanceVolumePerLevel;
				}
				return configs;
			}

			if (options.Mode == SweepMode.Absorption)
			{
				foreach (double absorptionDeltaThreshold in options.AbsorptionDeltaThresholdRatios)
				foreach (double absorptionPriceEfficiencyThreshold in options.AbsorptionPriceEfficiencyThresholds)
				foreach (double absorptionWickThreshold in options.AbsorptionWickThresholds)
				foreach (double absorptionWickNormalizationSpan in options.AbsorptionWickNormalizationSpans)
				foreach (double absorptionVolumeSpikeThreshold in options.AbsorptionVolumeSpikeThresholds)
				foreach (double rangeExpansionPenaltyThreshold in options.RangeExpansionPenaltyThresholds)
				{
					configs.Add(CreateDefaultConfig(defaults, defaults.SignalThreshold, defaults.ImbalanceWeight, defaults.AbsorptionWeight, defaults.ConfluenceBonus, defaults.NeutralityBuffer));
					ConfigSpec config = configs[configs.Count - 1];
					config.AbsorptionDeltaThresholdRatio = absorptionDeltaThreshold;
					config.AbsorptionPriceEfficiencyThreshold = absorptionPriceEfficiencyThreshold;
					config.AbsorptionWickThreshold = absorptionWickThreshold;
					config.AbsorptionWickNormalizationSpan = absorptionWickNormalizationSpan;
					config.AbsorptionVolumeSpikeThreshold = absorptionVolumeSpikeThreshold;
					config.RangeExpansionPenaltyThreshold = rangeExpansionPenaltyThreshold;
				}
				return configs;
			}

			if (options.Mode == SweepMode.Weights)
			{
				foreach (int signalThreshold in options.SignalThresholds)
				foreach (double imbalanceWeight in options.ImbalanceWeights)
				foreach (double absorptionWeight in options.AbsorptionWeights)
				foreach (double confluenceBonus in options.ConfluenceBonuses)
				foreach (double neutralityBuffer in options.NeutralityBuffers)
				{
					ConfigSpec config = CreateDefaultConfig(defaults, signalThreshold, imbalanceWeight, absorptionWeight, confluenceBonus, neutralityBuffer);
					if (config != null)
						configs.Add(config);
				}
				return configs;
			}

			foreach (int signalThreshold in options.SignalThresholds)
			foreach (double imbalanceWeight in options.ImbalanceWeights)
			foreach (double absorptionWeight in options.AbsorptionWeights)
			foreach (double confluenceBonus in options.ConfluenceBonuses)
			foreach (double imbalanceRatioThreshold in options.ImbalanceRatioThresholds)
			foreach (double imbalanceLevelNormalizationSpan in options.ImbalanceLevelNormalizationSpans)
			foreach (double deltaPerVolumeBaseline in options.DeltaPerVolumeBaselines)
			foreach (double deltaPerVolumeNormalizationSpan in options.DeltaPerVolumeNormalizationSpans)
			foreach (double imbalanceVolumeSpikeThreshold in options.ImbalanceVolumeSpikeThresholds)
			foreach (long minImbalanceVolumePerLevel in options.MinImbalanceVolumePerLevels)
			foreach (double absorptionDeltaThreshold in options.AbsorptionDeltaThresholdRatios)
			foreach (double absorptionPriceEfficiencyThreshold in options.AbsorptionPriceEfficiencyThresholds)
			foreach (double absorptionWickThreshold in options.AbsorptionWickThresholds)
			foreach (double absorptionWickNormalizationSpan in options.AbsorptionWickNormalizationSpans)
			foreach (double absorptionVolumeSpikeThreshold in options.AbsorptionVolumeSpikeThresholds)
			foreach (double rangeExpansionPenaltyThreshold in options.RangeExpansionPenaltyThresholds)
			foreach (double neutralityBuffer in options.NeutralityBuffers)
			{
				ConfigSpec config = CreateDefaultConfig(defaults, signalThreshold, imbalanceWeight, absorptionWeight, confluenceBonus, neutralityBuffer);
				if (config == null)
					continue;

				config.ImbalanceRatioThreshold = imbalanceRatioThreshold;
				config.ImbalanceLevelNormalizationSpan = imbalanceLevelNormalizationSpan;
				config.DeltaPerVolumeBaseline = deltaPerVolumeBaseline;
				config.DeltaPerVolumeNormalizationSpan = deltaPerVolumeNormalizationSpan;
				config.ImbalanceVolumeSpikeThreshold = imbalanceVolumeSpikeThreshold;
				config.MinImbalanceVolumePerLevel = minImbalanceVolumePerLevel;
				config.AbsorptionDeltaThresholdRatio = absorptionDeltaThreshold;
				config.AbsorptionPriceEfficiencyThreshold = absorptionPriceEfficiencyThreshold;
				config.AbsorptionWickThreshold = absorptionWickThreshold;
				config.AbsorptionWickNormalizationSpan = absorptionWickNormalizationSpan;
				config.AbsorptionVolumeSpikeThreshold = absorptionVolumeSpikeThreshold;
				config.RangeExpansionPenaltyThreshold = rangeExpansionPenaltyThreshold;
				configs.Add(config);
			}

			return configs;
		}

		private static ConfigSpec CreateDefaultConfig(EngineSettings defaults, int signalThreshold, double imbalanceWeight, double absorptionWeight, double confluenceBonus, double neutralityBuffer)
		{
			double remainingWeight = 1.0 - imbalanceWeight - absorptionWeight;
			if (remainingWeight < 0)
				return null;

			double failedBreakoutWeight = 0;
			double liquiditySweepWeight = 0;
			if (remainingWeight > 0)
			{
				const double defaultResidual = 0.20 + 0.25;
				failedBreakoutWeight = remainingWeight * (0.20 / defaultResidual);
				liquiditySweepWeight = remainingWeight * (0.25 / defaultResidual);
			}

			return new ConfigSpec
			{
				SignalThreshold = signalThreshold,
				ImbalanceWeight = imbalanceWeight,
				AbsorptionWeight = absorptionWeight,
				FailedBreakoutWeight = failedBreakoutWeight,
				LiquiditySweepWeight = liquiditySweepWeight,
				ConfluenceBonus = confluenceBonus,
				ImbalanceRatioThreshold = defaults.ImbalanceRatioThreshold,
				ImbalanceLevelNormalizationSpan = defaults.ImbalanceLevelNormalizationSpan,
				DeltaPerVolumeBaseline = defaults.DeltaPerVolumeBaseline,
				DeltaPerVolumeNormalizationSpan = defaults.DeltaPerVolumeNormalizationSpan,
				ImbalanceVolumeSpikeThreshold = defaults.ImbalanceVolumeSpikeThreshold,
				MinImbalanceVolumePerLevel = defaults.MinImbalanceVolumePerLevel,
				AbsorptionDeltaThresholdRatio = defaults.AbsorptionDeltaThresholdRatio,
				AbsorptionPriceEfficiencyThreshold = defaults.AbsorptionPriceEfficiencyThreshold,
				AbsorptionWickThreshold = defaults.AbsorptionWickThreshold,
				AbsorptionWickNormalizationSpan = defaults.AbsorptionWickNormalizationSpan,
				AbsorptionVolumeSpikeThreshold = defaults.AbsorptionVolumeSpikeThreshold,
				RangeExpansionPenaltyThreshold = defaults.RangeExpansionPenaltyThreshold,
				NeutralityBuffer = neutralityBuffer
			};
		}

		private ConfigSpec BuildDefaultConfig()
		{
			EngineSettings defaults = new EngineSettings();
			return new ConfigSpec
			{
				SignalThreshold = defaults.SignalThreshold,
				ImbalanceWeight = defaults.ImbalanceWeight,
				AbsorptionWeight = defaults.AbsorptionWeight,
				FailedBreakoutWeight = defaults.FailedBreakoutWeight,
				LiquiditySweepWeight = defaults.LiquiditySweepWeight,
				ConfluenceBonus = defaults.ConfluenceBonus,
				ImbalanceRatioThreshold = defaults.ImbalanceRatioThreshold,
				ImbalanceLevelNormalizationSpan = defaults.ImbalanceLevelNormalizationSpan,
				DeltaPerVolumeBaseline = defaults.DeltaPerVolumeBaseline,
				DeltaPerVolumeNormalizationSpan = defaults.DeltaPerVolumeNormalizationSpan,
				ImbalanceVolumeSpikeThreshold = defaults.ImbalanceVolumeSpikeThreshold,
				MinImbalanceVolumePerLevel = defaults.MinImbalanceVolumePerLevel,
				AbsorptionDeltaThresholdRatio = defaults.AbsorptionDeltaThresholdRatio,
				AbsorptionPriceEfficiencyThreshold = defaults.AbsorptionPriceEfficiencyThreshold,
				AbsorptionWickThreshold = defaults.AbsorptionWickThreshold,
				AbsorptionWickNormalizationSpan = defaults.AbsorptionWickNormalizationSpan,
				AbsorptionVolumeSpikeThreshold = defaults.AbsorptionVolumeSpikeThreshold,
				RangeExpansionPenaltyThreshold = defaults.RangeExpansionPenaltyThreshold,
				NeutralityBuffer = defaults.NeutralityBuffer
			};
		}

		private SweepSummary CreateSummary(ConfigSpec config, string recordType)
		{
			return new SweepSummary
			{
				RecordType = recordType,
				SignalThreshold = config.SignalThreshold,
				ImbalanceWeight = config.ImbalanceWeight,
				AbsorptionWeight = config.AbsorptionWeight,
				FailedBreakoutWeight = config.FailedBreakoutWeight,
				LiquiditySweepWeight = config.LiquiditySweepWeight,
				ConfluenceBonus = config.ConfluenceBonus,
				ImbalanceRatioThreshold = config.ImbalanceRatioThreshold,
				ImbalanceLevelNormalizationSpan = config.ImbalanceLevelNormalizationSpan,
				DeltaPerVolumeBaseline = config.DeltaPerVolumeBaseline,
				DeltaPerVolumeNormalizationSpan = config.DeltaPerVolumeNormalizationSpan,
				ImbalanceVolumeSpikeThreshold = config.ImbalanceVolumeSpikeThreshold,
				MinImbalanceVolumePerLevel = config.MinImbalanceVolumePerLevel,
				AbsorptionDeltaThresholdRatio = config.AbsorptionDeltaThresholdRatio,
				AbsorptionPriceEfficiencyThreshold = config.AbsorptionPriceEfficiencyThreshold,
				AbsorptionWickThreshold = config.AbsorptionWickThreshold,
				AbsorptionWickNormalizationSpan = config.AbsorptionWickNormalizationSpan,
				AbsorptionVolumeSpikeThreshold = config.AbsorptionVolumeSpikeThreshold,
				RangeExpansionPenaltyThreshold = config.RangeExpansionPenaltyThreshold,
				NeutralityBuffer = config.NeutralityBuffer,
				TargetTicks = options.TargetTicks,
				InvalidationTicks = options.InvalidationTicks,
				LookaheadBars = options.LookaheadBars
			};
		}

		private string BuildRankingJson(List<SweepSummary> summaries, string objective)
		{
			List<SweepSummary> ranked = new List<SweepSummary>();
			for (int index = 0; index < summaries.Count; index++)
			{
				if (summaries[index].RecordType == "configSummary" || summaries[index].RecordType == "walkForwardTest")
					ranked.Add(summaries[index]);
			}

			ranked.Sort(CompareSummaries);

			SweepSummary defaultSummary = null;
			for (int index = 0; index < summaries.Count; index++)
				if (summaries[index].RecordType == "defaultComparison")
					defaultSummary = summaries[index];

			StringBuilder builder = new StringBuilder(4096);
			builder.Append("{");
			builder.Append("\"recordType\":\"ranking\",");
			builder.Append("\"objective\":\"").Append(objective).Append("\",");
			builder.Append("\"topConfigurations\":[");
			int topCount = Math.Min(options.TopCount, ranked.Count);
			for (int index = 0; index < topCount; index++)
			{
				if (index > 0)
					builder.Append(",");
				builder.Append(ranked[index].ToJson());
			}
			builder.Append("],");
			builder.Append("\"defaultConfiguration\":");
			builder.Append(defaultSummary == null ? "{}" : defaultSummary.ToJson());
			builder.Append("}");
			return builder.ToString();
		}

		private static int CompareSummaries(SweepSummary left, SweepSummary right)
		{
			int compare = right.FinalScore.CompareTo(left.FinalScore);
			if (compare != 0)
				return compare;

			compare = left.AverageAdverseExcursionTicks.CompareTo(right.AverageAdverseExcursionTicks);
			if (compare != 0)
				return compare;

			compare = left.AverageTimeToMoveBars.CompareTo(right.AverageTimeToMoveBars);
			if (compare != 0)
				return compare;

			compare = right.Precision.CompareTo(left.Precision);
			if (compare != 0)
				return compare;

			return left.AverageLatencyMs.CompareTo(right.AverageLatencyMs);
		}

		private static TickData CloneTick(TickData tick)
		{
			return new TickData
			{
				TimestampUtc = tick.TimestampUtc,
				Instrument = tick.Instrument,
				Price = tick.Price,
				Volume = tick.Volume,
				Bid = tick.Bid,
				Ask = tick.Ask,
				IsBuyerInitiated = tick.IsBuyerInitiated
			};
		}

		private void WriteRecord(string json, StreamWriter writer)
		{
			Console.WriteLine(json);
			if (writer != null)
				writer.WriteLine(json);
		}

		private sealed class ConfigSpec
		{
			public int SignalThreshold { get; set; }
			public double ImbalanceWeight { get; set; }
			public double AbsorptionWeight { get; set; }
			public double FailedBreakoutWeight { get; set; }
			public double LiquiditySweepWeight { get; set; }
			public double ConfluenceBonus { get; set; }
			public double ImbalanceRatioThreshold { get; set; }
			public double ImbalanceLevelNormalizationSpan { get; set; }
			public double DeltaPerVolumeBaseline { get; set; }
			public double DeltaPerVolumeNormalizationSpan { get; set; }
			public double ImbalanceVolumeSpikeThreshold { get; set; }
			public long MinImbalanceVolumePerLevel { get; set; }
			public double AbsorptionDeltaThresholdRatio { get; set; }
			public double AbsorptionPriceEfficiencyThreshold { get; set; }
			public double AbsorptionWickThreshold { get; set; }
			public double AbsorptionWickNormalizationSpan { get; set; }
			public double AbsorptionVolumeSpikeThreshold { get; set; }
			public double RangeExpansionPenaltyThreshold { get; set; }
			public double NeutralityBuffer { get; set; }

			public EngineSettings ToSettings()
			{
				return new EngineSettings
				{
					SignalThreshold = SignalThreshold,
					ImbalanceWeight = ImbalanceWeight,
					AbsorptionWeight = AbsorptionWeight,
					FailedBreakoutWeight = FailedBreakoutWeight,
					LiquiditySweepWeight = LiquiditySweepWeight,
					ConfluenceBonus = ConfluenceBonus,
					ImbalanceRatioThreshold = ImbalanceRatioThreshold,
					ImbalanceLevelNormalizationSpan = ImbalanceLevelNormalizationSpan,
					DeltaPerVolumeBaseline = DeltaPerVolumeBaseline,
					DeltaPerVolumeNormalizationSpan = DeltaPerVolumeNormalizationSpan,
					ImbalanceVolumeSpikeThreshold = ImbalanceVolumeSpikeThreshold,
					MinImbalanceVolumePerLevel = MinImbalanceVolumePerLevel,
					AbsorptionDeltaThresholdRatio = AbsorptionDeltaThresholdRatio,
					AbsorptionPriceEfficiencyThreshold = AbsorptionPriceEfficiencyThreshold,
					AbsorptionWickThreshold = AbsorptionWickThreshold,
					AbsorptionWickNormalizationSpan = AbsorptionWickNormalizationSpan,
					AbsorptionVolumeSpikeThreshold = AbsorptionVolumeSpikeThreshold,
					RangeExpansionPenaltyThreshold = RangeExpansionPenaltyThreshold,
					NeutralityBuffer = NeutralityBuffer
				};
			}
		}

		private sealed class SessionMetrics
		{
			public SessionMetrics()
			{
				FullOrderFlow = new QualityMetrics();
				PriceOnly = new QualityMetrics();
			}

			public int CompletedBars { get; set; }
			public int SignalEvents { get; set; }
			public int MissedSignals { get; set; }
			public int WinningSignals { get; set; }
			public int FalsePositives { get; set; }
			public int UnresolvedSignals { get; set; }
			public int BullishSignals { get; set; }
			public int BearishSignals { get; set; }
			public int NeutralBars { get; set; }
			public int DirectionFlips { get; set; }
			public int FullOrderFlowPackets { get; set; }
			public int PriceOnlyPackets { get; set; }
			public double ScoreTotal { get; set; }
			public double LatencyTotal { get; set; }
			public int LatencyBars { get; set; }
			public double MaxLatencyMs { get; set; }
			public double TimeToMoveTotal { get; set; }
			public int TimeToMoveWins { get; set; }
			public double AdverseExcursionResolved { get; set; }
			public int ResolvedSignals { get; set; }
			public double Precision { get; set; }
			public double Recall { get; set; }
			public double F1 { get; set; }
			public QualityMetrics FullOrderFlow { get; private set; }
			public QualityMetrics PriceOnly { get; private set; }
		}

		private sealed class QualityMetrics
		{
			public int SignalEvents { get; set; }
			public int WinningSignals { get; set; }
			public int FalsePositives { get; set; }
			public int MissedSignals { get; set; }
			public int UnresolvedSignals { get; set; }
			public double TimeToMoveTotal { get; set; }
			public int TimeToMoveWins { get; set; }
			public double AdverseExcursionResolved { get; set; }
			public int ResolvedSignals { get; set; }
		}

		private sealed class SignalCandidate
		{
			public int BarIndex { get; set; }
			public string Direction { get; set; }
			public double EntryPrice { get; set; }
			public string DataQuality { get; set; }
		}

		private sealed class SignalEvaluation
		{
			public bool IsWin { get; set; }
			public bool IsLoss { get; set; }
			public int TimeToMoveBars { get; set; }
			public double AdverseExcursionTicks { get; set; }
		}

		private sealed class MissedOpportunity
		{
			public IntentDirection Direction { get; set; }
		}
	}
}
