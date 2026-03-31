using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Intent.Sweep
{
	internal sealed class SweepSummary
	{
		public SweepSummary()
		{
			FoldF1Scores = new List<double>();
			FullOrderFlow = new QualityBreakdown("FULL_ORDER_FLOW");
			PriceOnly = new QualityBreakdown("PRICE_ONLY");
		}

		public string RecordType { get; set; }
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
		public int TargetTicks { get; set; }
		public int InvalidationTicks { get; set; }
		public int LookaheadBars { get; set; }
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
		public double AverageScore { get; set; }
		public double AverageLatencyMs { get; set; }
		public double MaxLatencyMs { get; set; }
		public double HitRateProxy { get; set; }
		public double SignalEfficiency { get; set; }
		public double AverageTimeToMoveBars { get; set; }
		public double AverageAdverseExcursionTicks { get; set; }
		public double Precision { get; set; }
		public double Recall { get; set; }
		public double F1 { get; set; }
		public double StabilityPenalty { get; set; }
		public double FinalScore { get; set; }
		public int FoldCount { get; set; }
		public List<double> FoldF1Scores { get; private set; }
		public QualityBreakdown FullOrderFlow { get; private set; }
		public QualityBreakdown PriceOnly { get; private set; }

		public string ToJson()
		{
			StringBuilder builder = new StringBuilder(2048);
			builder.Append("{");
			AppendString(builder, "recordType", string.IsNullOrWhiteSpace(RecordType) ? "configSummary" : RecordType, true);
			AppendNumber(builder, "signalThreshold", SignalThreshold, true);
			AppendNumber(builder, "imbalanceWeight", ImbalanceWeight, true);
			AppendNumber(builder, "absorptionWeight", AbsorptionWeight, true);
			AppendNumber(builder, "failedBreakoutWeight", FailedBreakoutWeight, true);
			AppendNumber(builder, "liquiditySweepWeight", LiquiditySweepWeight, true);
			AppendNumber(builder, "confluenceBonus", ConfluenceBonus, true);
			AppendNumber(builder, "imbalanceRatioThreshold", ImbalanceRatioThreshold, true);
			AppendNumber(builder, "imbalanceLevelNormalizationSpan", ImbalanceLevelNormalizationSpan, true);
			AppendNumber(builder, "deltaPerVolumeBaseline", DeltaPerVolumeBaseline, true);
			AppendNumber(builder, "deltaPerVolumeNormalizationSpan", DeltaPerVolumeNormalizationSpan, true);
			AppendNumber(builder, "imbalanceVolumeSpikeThreshold", ImbalanceVolumeSpikeThreshold, true);
			AppendNumber(builder, "minImbalanceVolumePerLevel", MinImbalanceVolumePerLevel, true);
			AppendNumber(builder, "absorptionDeltaThresholdRatio", AbsorptionDeltaThresholdRatio, true);
			AppendNumber(builder, "absorptionPriceEfficiencyThreshold", AbsorptionPriceEfficiencyThreshold, true);
			AppendNumber(builder, "absorptionWickThreshold", AbsorptionWickThreshold, true);
			AppendNumber(builder, "absorptionWickNormalizationSpan", AbsorptionWickNormalizationSpan, true);
			AppendNumber(builder, "absorptionVolumeSpikeThreshold", AbsorptionVolumeSpikeThreshold, true);
			AppendNumber(builder, "rangeExpansionPenaltyThreshold", RangeExpansionPenaltyThreshold, true);
			AppendNumber(builder, "neutralityBuffer", NeutralityBuffer, true);
			AppendNumber(builder, "targetTicks", TargetTicks, true);
			AppendNumber(builder, "invalidationTicks", InvalidationTicks, true);
			AppendNumber(builder, "lookaheadBars", LookaheadBars, true);
			AppendNumber(builder, "completedBars", CompletedBars, true);
			AppendNumber(builder, "signalEvents", SignalEvents, true);
			AppendNumber(builder, "missedSignals", MissedSignals, true);
			AppendNumber(builder, "winningSignals", WinningSignals, true);
			AppendNumber(builder, "falsePositives", FalsePositives, true);
			AppendNumber(builder, "unresolvedSignals", UnresolvedSignals, true);
			AppendNumber(builder, "averageScore", AverageScore, true);
			AppendNumber(builder, "averageLatencyMs", AverageLatencyMs, true);
			AppendNumber(builder, "maxLatencyMs", MaxLatencyMs, true);
			AppendNumber(builder, "bullishSignals", BullishSignals, true);
			AppendNumber(builder, "bearishSignals", BearishSignals, true);
			AppendNumber(builder, "neutralBars", NeutralBars, true);
			AppendNumber(builder, "directionFlips", DirectionFlips, true);
			AppendNumber(builder, "hitRateProxy", HitRateProxy, true);
			AppendNumber(builder, "signalEfficiency", SignalEfficiency, true);
			AppendNumber(builder, "averageTimeToMoveBars", AverageTimeToMoveBars, true);
			AppendNumber(builder, "averageAdverseExcursionTicks", AverageAdverseExcursionTicks, true);
			AppendNumber(builder, "precision", Precision, true);
			AppendNumber(builder, "recall", Recall, true);
			AppendNumber(builder, "f1", F1, true);
			AppendNumber(builder, "stabilityPenalty", StabilityPenalty, true);
			AppendNumber(builder, "finalScore", FinalScore, true);
			AppendNumber(builder, "foldCount", FoldCount, true);
			AppendNumber(builder, "fullOrderFlowPackets", FullOrderFlowPackets, true);
			AppendNumber(builder, "priceOnlyPackets", PriceOnlyPackets, true);
			AppendNumberArray(builder, "foldF1Scores", FoldF1Scores, true);
			AppendQuality(builder, "fullOrderFlow", FullOrderFlow, true);
			AppendQuality(builder, "priceOnly", PriceOnly, false);
			builder.Append("}");
			return builder.ToString();
		}

		private static void AppendString(StringBuilder builder, string name, string value, bool comma)
		{
			builder.Append("\"").Append(name).Append("\":\"").Append(Escape(value)).Append("\"");
			if (comma)
				builder.Append(",");
		}

		private static void AppendNumber(StringBuilder builder, string name, double value, bool comma)
		{
			builder.Append("\"").Append(name).Append("\":").Append(value.ToString("0.######", CultureInfo.InvariantCulture));
			if (comma)
				builder.Append(",");
		}

		private static void AppendNumberArray(StringBuilder builder, string name, List<double> values, bool comma)
		{
			builder.Append("\"").Append(name).Append("\":[");
			for (int index = 0; index < (values == null ? 0 : values.Count); index++)
			{
				if (index > 0)
					builder.Append(",");
				builder.Append(values[index].ToString("0.######", CultureInfo.InvariantCulture));
			}
			builder.Append("]");
			if (comma)
				builder.Append(",");
		}

		private static void AppendQuality(StringBuilder builder, string name, QualityBreakdown quality, bool comma)
		{
			builder.Append("\"").Append(name).Append("\":");
			builder.Append(quality == null ? "{}" : quality.ToJson());
			if (comma)
				builder.Append(",");
		}

		private static string Escape(string value)
		{
			return string.IsNullOrEmpty(value) ? string.Empty : value.Replace("\\", "\\\\").Replace("\"", "\\\"");
		}
	}

	internal sealed class QualityBreakdown
	{
		public QualityBreakdown(string name)
		{
			Name = name;
		}

		public string Name { get; private set; }
		public int SignalEvents { get; set; }
		public int WinningSignals { get; set; }
		public int FalsePositives { get; set; }
		public int MissedSignals { get; set; }
		public int UnresolvedSignals { get; set; }
		public double HitRateProxy { get; set; }
		public double SignalEfficiency { get; set; }
		public double AverageTimeToMoveBars { get; set; }
		public double AverageAdverseExcursionTicks { get; set; }
		public double Precision { get; set; }
		public double Recall { get; set; }
		public double F1 { get; set; }

		public string ToJson()
		{
			StringBuilder builder = new StringBuilder(320);
			builder.Append("{");
			builder.Append("\"name\":\"").Append(Name).Append("\",");
			builder.Append("\"signalEvents\":").Append(SignalEvents.ToString(CultureInfo.InvariantCulture)).Append(",");
			builder.Append("\"winningSignals\":").Append(WinningSignals.ToString(CultureInfo.InvariantCulture)).Append(",");
			builder.Append("\"falsePositives\":").Append(FalsePositives.ToString(CultureInfo.InvariantCulture)).Append(",");
			builder.Append("\"missedSignals\":").Append(MissedSignals.ToString(CultureInfo.InvariantCulture)).Append(",");
			builder.Append("\"unresolvedSignals\":").Append(UnresolvedSignals.ToString(CultureInfo.InvariantCulture)).Append(",");
			builder.Append("\"hitRateProxy\":").Append(HitRateProxy.ToString("0.######", CultureInfo.InvariantCulture)).Append(",");
			builder.Append("\"signalEfficiency\":").Append(SignalEfficiency.ToString("0.######", CultureInfo.InvariantCulture)).Append(",");
			builder.Append("\"averageTimeToMoveBars\":").Append(AverageTimeToMoveBars.ToString("0.######", CultureInfo.InvariantCulture)).Append(",");
			builder.Append("\"averageAdverseExcursionTicks\":").Append(AverageAdverseExcursionTicks.ToString("0.######", CultureInfo.InvariantCulture)).Append(",");
			builder.Append("\"precision\":").Append(Precision.ToString("0.######", CultureInfo.InvariantCulture)).Append(",");
			builder.Append("\"recall\":").Append(Recall.ToString("0.######", CultureInfo.InvariantCulture)).Append(",");
			builder.Append("\"f1\":").Append(F1.ToString("0.######", CultureInfo.InvariantCulture));
			builder.Append("}");
			return builder.ToString();
		}
	}
}
