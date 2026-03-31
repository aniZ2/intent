using Intent.Engine.Models;
using System.Text;

namespace Intent.Engine.Signals
{
	public enum IntentDirection
	{
		Neutral = 0,
		Bullish = 1,
		Bearish = -1
	}

	public enum IntentSignalType
	{
		OrderFlowImbalance,
		Absorption,
		FailedBreakout,
		LiquiditySweep
	}

	public sealed class SignalFactor
	{
		public string Name { get; set; }
		public double RawValue { get; set; }
		public double NormalizedValue { get; set; }
		public double Weight { get; set; }
		public double Contribution { get; set; }
		public string Detail { get; set; }
	}

	public sealed class SignalScore
	{
		public SignalScore(IntentSignalType signalType)
		{
			SignalType = signalType;
			BullishFactors = new SignalFactor[0];
			BearishFactors = new SignalFactor[0];
		}

		public IntentSignalType SignalType { get; private set; }
		public double Bullish { get; private set; }
		public double Bearish { get; private set; }
		public string BullishReason { get; private set; }
		public string BearishReason { get; private set; }
		public SignalFactor[] BullishFactors { get; private set; }
		public SignalFactor[] BearishFactors { get; private set; }

		public void SetScores(double bullish, double bearish, string bullishReason, string bearishReason, SignalFactor[] bullishFactors, SignalFactor[] bearishFactors)
		{
			Bullish = SignalMath.Clamp100(bullish);
			Bearish = SignalMath.Clamp100(bearish);
			BullishReason = bullishReason;
			BearishReason = bearishReason;
			BullishFactors = bullishFactors ?? new SignalFactor[0];
			BearishFactors = bearishFactors ?? new SignalFactor[0];
		}

		public void ScaleScore(IntentDirection direction, double multiplier, string factorName, string detail)
		{
			multiplier = SignalMath.Clamp01(multiplier);
			if (direction == IntentDirection.Bullish)
			{
				Bullish *= multiplier;
				SignalFactor[] factors = BullishFactors;
				AppendFactor(ref factors, factorName, multiplier, detail, Bullish);
				BullishFactors = factors;
			}
			else if (direction == IntentDirection.Bearish)
			{
				Bearish *= multiplier;
				SignalFactor[] factors = BearishFactors;
				AppendFactor(ref factors, factorName, multiplier, detail, Bearish);
				BearishFactors = factors;
			}
		}

		public double GetScore(IntentDirection direction)
		{
			return direction == IntentDirection.Bullish ? Bullish : Bearish;
		}

		public string GetReason(IntentDirection direction)
		{
			return direction == IntentDirection.Bullish ? BullishReason : BearishReason;
		}

		public bool IsTriggered(IntentDirection direction, double threshold)
		{
			return GetScore(direction) >= threshold;
		}

		public SignalScorePacket ToPacket()
		{
			return new SignalScorePacket
			{
				SignalType = SignalType.ToString(),
				BullishScore = Bullish,
				BearishScore = Bearish,
				BullishReason = BullishReason,
				BearishReason = BearishReason,
				BullishFactors = BullishFactors,
				BearishFactors = BearishFactors
			};
		}

		private static void AppendFactor(ref SignalFactor[] factors, string factorName, double multiplier, string detail, double adjustedScore)
		{
			SignalFactor[] source = factors ?? new SignalFactor[0];
			SignalFactor[] next = new SignalFactor[source.Length + 1];
			for (int index = 0; index < source.Length; index++)
				next[index] = source[index];

			next[source.Length] = new SignalFactor
			{
				Name = factorName,
				RawValue = multiplier,
				NormalizedValue = multiplier,
				Weight = 1.0,
				Contribution = adjustedScore,
				Detail = detail
			};
			factors = next;
		}
	}

	public sealed class SignalScorePacket
	{
		public string SignalType { get; set; }
		public double BullishScore { get; set; }
		public double BearishScore { get; set; }
		public string BullishReason { get; set; }
		public string BearishReason { get; set; }
		public SignalFactor[] BullishFactors { get; set; }
		public SignalFactor[] BearishFactors { get; set; }
	}

	public sealed class DecisionPacket
	{
		public string TimestampUtc { get; set; }
		public string Instrument { get; set; }
		public string Session { get; set; }
		public string EventType { get; set; }
		public double Score { get; set; }
		public double IntentScore { get; set; }
		public double BullScore { get; set; }
		public double BearScore { get; set; }
		public string Bias { get; set; }
		public string Direction { get; set; }
		public string Confidence { get; set; }
		public string DominantReason { get; set; }
		public string DominantSignalType { get; set; }
		public string Invalidation { get; set; }
		public string[] TargetZones { get; set; }
		public double LatencyMs { get; set; }
		public string DataQuality { get; set; }
		public bool HasOrderFlow { get; set; }
		public SignalFactor[] Factors { get; set; }
		public SignalFactor[] BullishScoreFactors { get; set; }
		public SignalFactor[] BearishScoreFactors { get; set; }
		public SignalScorePacket[] Signals { get; set; }

		public string ToJson()
		{
			StringBuilder builder = new StringBuilder(1024);
			builder.Append("{");
			AppendProperty(builder, "timestampUtc", TimestampUtc, true);
			AppendProperty(builder, "instrument", Instrument, true);
			AppendProperty(builder, "session", Session, true);
			AppendProperty(builder, "eventType", EventType, true);
			AppendProperty(builder, "score", Score);
			AppendProperty(builder, "intentScore", IntentScore);
			AppendProperty(builder, "bullScore", BullScore);
			AppendProperty(builder, "bearScore", BearScore);
			AppendProperty(builder, "bias", Bias, true);
			AppendProperty(builder, "direction", Direction, true);
			AppendProperty(builder, "confidence", Confidence, true);
			AppendProperty(builder, "dominantReason", DominantReason, true);
			AppendProperty(builder, "dominantSignalType", DominantSignalType, true);
			AppendProperty(builder, "invalidation", Invalidation, true);
			AppendProperty(builder, "latencyMs", LatencyMs);
			AppendProperty(builder, "dataQuality", DataQuality, true);
			AppendProperty(builder, "hasOrderFlow", HasOrderFlow);
			AppendSignalFactors(builder, "factors", Factors);
			builder.Append(",");
			AppendStringArray(builder, "targetZones", TargetZones);
			builder.Append(",");
			AppendSignalFactors(builder, "bullishScoreFactors", BullishScoreFactors);
			builder.Append(",");
			AppendSignalFactors(builder, "bearishScoreFactors", BearishScoreFactors);
			builder.Append(",");
			AppendSignalPackets(builder, "signals", Signals);
			builder.Append("}");
			return builder.ToString();
		}

		private static void AppendStringArray(StringBuilder builder, string name, string[] values)
		{
			builder.Append("\"").Append(name).Append("\":[");
			for (int index = 0; index < (values == null ? 0 : values.Length); index++)
			{
				if (index > 0)
					builder.Append(",");

				builder.Append("\"").Append(EscapeJson(values[index])).Append("\"");
			}

			builder.Append("]");
		}

		private static void AppendSignalPackets(StringBuilder builder, string name, SignalScorePacket[] signals)
		{
			builder.Append("\"").Append(name).Append("\":[");
			for (int index = 0; index < (signals == null ? 0 : signals.Length); index++)
			{
				if (index > 0)
					builder.Append(",");

				SignalScorePacket signal = signals[index];
				builder.Append("{");
				AppendProperty(builder, "signalType", signal.SignalType, true);
				AppendProperty(builder, "bullishScore", signal.BullishScore);
				AppendProperty(builder, "bearishScore", signal.BearishScore);
				AppendProperty(builder, "bullishReason", signal.BullishReason, true);
				AppendProperty(builder, "bearishReason", signal.BearishReason, true);
				AppendSignalFactors(builder, "bullishFactors", signal.BullishFactors);
				builder.Append(",");
				AppendSignalFactors(builder, "bearishFactors", signal.BearishFactors);
				builder.Append("}");
			}

			builder.Append("]");
		}

		private static void AppendSignalFactors(StringBuilder builder, string name, SignalFactor[] factors)
		{
			builder.Append("\"").Append(name).Append("\":[");
			for (int index = 0; index < (factors == null ? 0 : factors.Length); index++)
			{
				if (index > 0)
					builder.Append(",");

				SignalFactor factor = factors[index];
				builder.Append("{");
				AppendProperty(builder, "name", factor.Name, true);
				AppendProperty(builder, "rawValue", factor.RawValue);
				AppendProperty(builder, "normalizedValue", factor.NormalizedValue);
				AppendProperty(builder, "weight", factor.Weight);
				AppendProperty(builder, "contribution", factor.Contribution);
				AppendProperty(builder, "detail", factor.Detail, true, false);
				builder.Append("}");
			}

			builder.Append("]");
		}

		private static void AppendProperty(StringBuilder builder, string name, string value, bool stringValue)
		{
			AppendProperty(builder, name, value, stringValue, true);
		}

		private static void AppendProperty(StringBuilder builder, string name, string value, bool stringValue, bool appendComma)
		{
			builder.Append("\"").Append(name).Append("\":");
			if (stringValue)
				builder.Append("\"").Append(EscapeJson(value)).Append("\"");
			else
				builder.Append(value);

			if (appendComma)
				builder.Append(",");
		}

		private static void AppendProperty(StringBuilder builder, string name, double value)
		{
			builder.Append("\"").Append(name).Append("\":").Append(value.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture)).Append(",");
		}

		private static void AppendProperty(StringBuilder builder, string name, bool value)
		{
			builder.Append("\"").Append(name).Append("\":").Append(value ? "true" : "false").Append(",");
		}

		private static string EscapeJson(string value)
		{
			if (string.IsNullOrEmpty(value))
				return string.Empty;

			return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
		}
	}

	public sealed class SignalResult
	{
		public SignalResult()
		{
			Imbalance = new SignalScore(IntentSignalType.OrderFlowImbalance);
			Absorption = new SignalScore(IntentSignalType.Absorption);
			FailedBreakout = new SignalScore(IntentSignalType.FailedBreakout);
			LiquiditySweep = new SignalScore(IntentSignalType.LiquiditySweep);
			Signals = new[] { Imbalance, Absorption, FailedBreakout, LiquiditySweep };
			BullishScoreFactors = new SignalFactor[0];
			BearishScoreFactors = new SignalFactor[0];
		}

		public BarData Bar { get; set; }
		public SignalScore Imbalance { get; private set; }
		public SignalScore Absorption { get; private set; }
		public SignalScore FailedBreakout { get; private set; }
		public SignalScore LiquiditySweep { get; private set; }
		public SignalScore[] Signals { get; private set; }
		public double BullScore { get; set; }
		public double BearScore { get; set; }
		public double IntentScore { get; set; }
		public IntentDirection Direction { get; set; }
		public string DominantReason { get; set; }
		public SignalFactor[] BullishScoreFactors { get; set; }
		public SignalFactor[] BearishScoreFactors { get; set; }
		public IntentDirection PriorSignalDirection { get; set; }
		public double PriorIntentScore { get; set; }

		public SignalScore GetDominantSignal(IntentDirection direction)
		{
			SignalScore winner = Signals[0];
			double best = winner.GetScore(direction);

			for (int i = 1; i < Signals.Length; i++)
			{
				double candidate = Signals[i].GetScore(direction);
				if (candidate <= best)
					continue;

				best = candidate;
				winner = Signals[i];
			}

			return winner;
		}

		public SignalFactor[] GetScoreFactors(IntentDirection direction)
		{
			return direction == IntentDirection.Bullish ? BullishScoreFactors : BearishScoreFactors;
		}

		public DecisionPacket ToDecisionPacket()
		{
			IntentDirection packetDirection = Direction == IntentDirection.Neutral
				? (BullScore >= BearScore ? IntentDirection.Bullish : IntentDirection.Bearish)
				: Direction;

			SignalScore dominantSignal = GetDominantSignal(packetDirection);
			return new DecisionPacket
			{
				TimestampUtc = Bar == null ? string.Empty : Bar.TimestampUtc.ToString("o"),
				Instrument = string.Empty,
				Session = Bar == null ? string.Empty : Bar.TimestampUtc.ToString("yyyy-MM-dd"),
				EventType = "analysis",
				Score = IntentScore,
				IntentScore = IntentScore,
				BullScore = BullScore,
				BearScore = BearScore,
				Bias = packetDirection.ToString(),
				Direction = Direction.ToString(),
				Confidence = ClassifyConfidence(IntentScore),
				DominantReason = DominantReason,
				DominantSignalType = dominantSignal == null ? string.Empty : dominantSignal.SignalType.ToString(),
				Invalidation = BuildInvalidation(packetDirection),
				TargetZones = BuildTargetZones(packetDirection),
				LatencyMs = 0,
				DataQuality = Bar != null && Bar.OrderFlow != null && Bar.OrderFlow.IsAvailable ? "FULL_ORDER_FLOW" : "PRICE_ONLY",
				HasOrderFlow = Bar != null && Bar.OrderFlow != null && Bar.OrderFlow.IsAvailable,
				Factors = GetScoreFactors(packetDirection),
				BullishScoreFactors = BullishScoreFactors,
				BearishScoreFactors = BearishScoreFactors,
				Signals = new[]
				{
					Imbalance.ToPacket(),
					Absorption.ToPacket(),
					FailedBreakout.ToPacket(),
					LiquiditySweep.ToPacket()
				}
			};
		}

		private string BuildInvalidation(IntentDirection packetDirection)
		{
			if (Bar == null)
				return string.Empty;

			if (packetDirection == IntentDirection.Bullish)
				return string.Format(System.Globalization.CultureInfo.InvariantCulture, "Acceptance back below {0:0.#####}", Bar.PriorSwingLow);
			if (packetDirection == IntentDirection.Bearish)
				return string.Format(System.Globalization.CultureInfo.InvariantCulture, "Acceptance back above {0:0.#####}", Bar.PriorSwingHigh);

			return string.Format(
				System.Globalization.CultureInfo.InvariantCulture,
				"Break outside {0:0.#####}-{1:0.#####}",
				Bar.Low,
				Bar.High);
		}

		private string[] BuildTargetZones(IntentDirection packetDirection)
		{
			if (Bar == null)
				return new string[0];

			if (packetDirection == IntentDirection.Bullish)
			{
				return new[]
				{
					string.Format(System.Globalization.CultureInfo.InvariantCulture, "prior-high:{0:0.#####}", Bar.PriorSwingHigh),
					string.Format(System.Globalization.CultureInfo.InvariantCulture, "bar-high:{0:0.#####}", Bar.High)
				};
			}

			if (packetDirection == IntentDirection.Bearish)
			{
				return new[]
				{
					string.Format(System.Globalization.CultureInfo.InvariantCulture, "prior-low:{0:0.#####}", Bar.PriorSwingLow),
					string.Format(System.Globalization.CultureInfo.InvariantCulture, "bar-low:{0:0.#####}", Bar.Low)
				};
			}

			return new[]
			{
				string.Format(System.Globalization.CultureInfo.InvariantCulture, "bar-high:{0:0.#####}", Bar.High),
				string.Format(System.Globalization.CultureInfo.InvariantCulture, "bar-low:{0:0.#####}", Bar.Low)
			};
		}

		private static string ClassifyConfidence(double score)
		{
			if (score >= 80)
				return "HIGH";
			if (score >= 60)
				return "MEDIUM";
			return "LOW";
		}
	}
}
