using System;
using System.Globalization;
using System.IO;

namespace Intent.Sweep
{
	internal enum SweepMode
	{
		Combined,
		Imbalance,
		Absorption,
		Weights
	}

	internal sealed class SweepOptions
	{
		public SweepOptions()
		{
			Mode = SweepMode.Combined;
			BarSeconds = 60;
			TickSize = 0.25;
			TargetTicks = 4;
			InvalidationTicks = 4;
			LookaheadBars = 8;
			TopCount = 3;
			TrainWindowSessions = 4;
			SignalThresholds = new[] { 60 };
			ImbalanceWeights = new[] { 0.35 };
			AbsorptionWeights = new[] { 0.20 };
			ConfluenceBonuses = new[] { 8.0 };
			ImbalanceRatioThresholds = new[] { 2.5 };
			ImbalanceLevelNormalizationSpans = new[] { 4.0 };
			DeltaPerVolumeBaselines = new[] { 0.10 };
			DeltaPerVolumeNormalizationSpans = new[] { 0.40 };
			ImbalanceVolumeSpikeThresholds = new[] { 1.15 };
			MinImbalanceVolumePerLevels = new[] { 15L };
			AbsorptionDeltaThresholdRatios = new[] { 0.22 };
			AbsorptionPriceEfficiencyThresholds = new[] { 0.35 };
			AbsorptionWickThresholds = new[] { 0.35 };
			AbsorptionWickNormalizationSpans = new[] { 0.65 };
			AbsorptionVolumeSpikeThresholds = new[] { 1.20 };
			RangeExpansionPenaltyThresholds = new[] { 1.25 };
			NeutralityBuffers = new[] { 5.0 };
		}

		public string InputPath { get; set; }
		public string OutputPath { get; set; }
		public SweepMode Mode { get; set; }
		public int BarSeconds { get; set; }
		public double TickSize { get; set; }
		public int TargetTicks { get; set; }
		public int InvalidationTicks { get; set; }
		public int LookaheadBars { get; set; }
		public int TopCount { get; set; }
		public int TrainWindowSessions { get; set; }
		public int[] SignalThresholds { get; set; }
		public double[] ImbalanceWeights { get; set; }
		public double[] AbsorptionWeights { get; set; }
		public double[] ConfluenceBonuses { get; set; }
		public double[] ImbalanceRatioThresholds { get; set; }
		public double[] ImbalanceLevelNormalizationSpans { get; set; }
		public double[] DeltaPerVolumeBaselines { get; set; }
		public double[] DeltaPerVolumeNormalizationSpans { get; set; }
		public double[] ImbalanceVolumeSpikeThresholds { get; set; }
		public long[] MinImbalanceVolumePerLevels { get; set; }
		public double[] AbsorptionDeltaThresholdRatios { get; set; }
		public double[] AbsorptionPriceEfficiencyThresholds { get; set; }
		public double[] AbsorptionWickThresholds { get; set; }
		public double[] AbsorptionWickNormalizationSpans { get; set; }
		public double[] AbsorptionVolumeSpikeThresholds { get; set; }
		public double[] RangeExpansionPenaltyThresholds { get; set; }
		public double[] NeutralityBuffers { get; set; }

		public static SweepOptions Parse(string[] args)
		{
			SweepOptions options = new SweepOptions();
			for (int index = 0; index < (args == null ? 0 : args.Length); index++)
			{
				string argument = args[index];
				if (string.Equals(argument, "--input", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
					options.InputPath = args[++index];
				else if (string.Equals(argument, "--output", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
					options.OutputPath = args[++index];
				else if (string.Equals(argument, "--mode", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
					options.Mode = ParseMode(args[++index]);
				else if (string.Equals(argument, "--bar-seconds", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
					options.BarSeconds = ParseInt(args[++index], 1, 86400, "bar-seconds");
				else if (string.Equals(argument, "--tick-size", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
					options.TickSize = ParseDouble(args[++index], 0.0000001, "tick-size");
				else if (string.Equals(argument, "--target-ticks", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
					options.TargetTicks = ParseInt(args[++index], 1, 1000, "target-ticks");
				else if (string.Equals(argument, "--invalidation-ticks", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
					options.InvalidationTicks = ParseInt(args[++index], 1, 1000, "invalidation-ticks");
				else if (string.Equals(argument, "--lookahead-bars", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
					options.LookaheadBars = ParseInt(args[++index], 1, 10000, "lookahead-bars");
				else if (string.Equals(argument, "--top-count", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
					options.TopCount = ParseInt(args[++index], 1, 100, "top-count");
				else if (string.Equals(argument, "--train-window-sessions", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
					options.TrainWindowSessions = ParseInt(args[++index], 1, 1000, "train-window-sessions");
				else if (string.Equals(argument, "--signal-thresholds", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
					options.SignalThresholds = ParseIntList(args[++index], 1, 100, "signal-thresholds");
				else if (string.Equals(argument, "--imbalance-weights", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
					options.ImbalanceWeights = ParseDoubleList(args[++index], 0, 1, "imbalance-weights");
				else if (string.Equals(argument, "--absorption-weights", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
					options.AbsorptionWeights = ParseDoubleList(args[++index], 0, 1, "absorption-weights");
				else if (string.Equals(argument, "--confluence-bonuses", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
					options.ConfluenceBonuses = ParseDoubleList(args[++index], 0, 1000, "confluence-bonuses");
				else if (string.Equals(argument, "--imbalance-ratio-thresholds", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
					options.ImbalanceRatioThresholds = ParseDoubleList(args[++index], 0, 1000, "imbalance-ratio-thresholds");
				else if (string.Equals(argument, "--imbalance-level-spans", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
					options.ImbalanceLevelNormalizationSpans = ParseDoubleList(args[++index], 0, 1000, "imbalance-level-spans");
				else if (string.Equals(argument, "--delta-per-volume-baselines", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
					options.DeltaPerVolumeBaselines = ParseDoubleList(args[++index], 0, 1000, "delta-per-volume-baselines");
				else if (string.Equals(argument, "--delta-per-volume-spans", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
					options.DeltaPerVolumeNormalizationSpans = ParseDoubleList(args[++index], 0, 1000, "delta-per-volume-spans");
				else if (string.Equals(argument, "--imbalance-volume-spike-thresholds", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
					options.ImbalanceVolumeSpikeThresholds = ParseDoubleList(args[++index], 0, 1000, "imbalance-volume-spike-thresholds");
				else if (string.Equals(argument, "--min-imbalance-volume-per-levels", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
					options.MinImbalanceVolumePerLevels = ParseLongList(args[++index], 0, 1000000, "min-imbalance-volume-per-levels");
				else if (string.Equals(argument, "--absorption-delta-thresholds", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
					options.AbsorptionDeltaThresholdRatios = ParseDoubleList(args[++index], 0, 10, "absorption-delta-thresholds");
				else if (string.Equals(argument, "--absorption-price-efficiency-thresholds", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
					options.AbsorptionPriceEfficiencyThresholds = ParseDoubleList(args[++index], 0, 1000, "absorption-price-efficiency-thresholds");
				else if (string.Equals(argument, "--absorption-wick-thresholds", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
					options.AbsorptionWickThresholds = ParseDoubleList(args[++index], 0, 1000, "absorption-wick-thresholds");
				else if (string.Equals(argument, "--absorption-wick-spans", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
					options.AbsorptionWickNormalizationSpans = ParseDoubleList(args[++index], 0, 1000, "absorption-wick-spans");
				else if (string.Equals(argument, "--absorption-volume-spike-thresholds", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
					options.AbsorptionVolumeSpikeThresholds = ParseDoubleList(args[++index], 0, 1000, "absorption-volume-spike-thresholds");
				else if (string.Equals(argument, "--range-expansion-penalty-thresholds", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
					options.RangeExpansionPenaltyThresholds = ParseDoubleList(args[++index], 0, 1000, "range-expansion-penalty-thresholds");
				else if (string.Equals(argument, "--neutrality-buffers", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
					options.NeutralityBuffers = ParseDoubleList(args[++index], 0, 100, "neutrality-buffers");
				else
					throw new InvalidOperationException("Usage: Intent.Sweep --input ticks.ndjson|sessionDir [--mode combined|imbalance|absorption|weights] [--output sweep.ndjson] [--signal-thresholds 55,60,65] [--imbalance-weights 0.25,0.35] [--absorption-weights 0.15,0.20,0.25] [--confluence-bonuses 4,8,12] [--imbalance-ratio-thresholds 2.0,2.5,3.0] [--imbalance-level-spans 3,4,6] [--delta-per-volume-baselines 0.05,0.10] [--delta-per-volume-spans 0.25,0.40] [--imbalance-volume-spike-thresholds 1.0,1.15] [--min-imbalance-volume-per-levels 10,15,25] [--absorption-delta-thresholds 0.18,0.22] [--absorption-price-efficiency-thresholds 0.25,0.35] [--absorption-wick-thresholds 0.25,0.35] [--absorption-wick-spans 0.45,0.65] [--absorption-volume-spike-thresholds 1.0,1.2] [--range-expansion-penalty-thresholds 1.1,1.25] [--neutrality-buffers 4,5,6] [--target-ticks 4] [--invalidation-ticks 4] [--lookahead-bars 8] [--top-count 3] [--train-window-sessions 4]");
			}

			if (string.IsNullOrWhiteSpace(options.InputPath) || (!File.Exists(options.InputPath) && !Directory.Exists(options.InputPath)))
				throw new InvalidOperationException("Sweep input path must be an NDJSON file or a directory of NDJSON files.");

			return options;
		}

		private static int ParseInt(string raw, int min, int max, string name)
		{
			int value;
			if (!int.TryParse(raw, out value) || value < min || value > max)
				throw new InvalidOperationException("Invalid " + name + ": " + raw);
			return value;
		}

		private static double ParseDouble(string raw, double min, string name)
		{
			double value;
			if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value) || value < min)
				throw new InvalidOperationException("Invalid " + name + ": " + raw);
			return value;
		}

		private static SweepMode ParseMode(string raw)
		{
			if (string.Equals(raw, "combined", StringComparison.OrdinalIgnoreCase))
				return SweepMode.Combined;
			if (string.Equals(raw, "imbalance", StringComparison.OrdinalIgnoreCase))
				return SweepMode.Imbalance;
			if (string.Equals(raw, "absorption", StringComparison.OrdinalIgnoreCase))
				return SweepMode.Absorption;
			if (string.Equals(raw, "weights", StringComparison.OrdinalIgnoreCase))
				return SweepMode.Weights;

			throw new InvalidOperationException("Invalid mode: " + raw);
		}

		private static int[] ParseIntList(string raw, int min, int max, string name)
		{
			string[] parts = raw.Split(',');
			int[] values = new int[parts.Length];
			for (int i = 0; i < parts.Length; i++)
				values[i] = ParseInt(parts[i].Trim(), min, max, name);
			return values;
		}

		private static long[] ParseLongList(string raw, long min, long max, string name)
		{
			string[] parts = raw.Split(',');
			long[] values = new long[parts.Length];
			for (int i = 0; i < parts.Length; i++)
			{
				long value;
				if (!long.TryParse(parts[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) || value < min || value > max)
					throw new InvalidOperationException("Invalid " + name + ": " + parts[i]);
				values[i] = value;
			}

			return values;
		}

		private static double[] ParseDoubleList(string raw, double min, double max, string name)
		{
			string[] parts = raw.Split(',');
			double[] values = new double[parts.Length];
			for (int i = 0; i < parts.Length; i++)
			{
				values[i] = ParseDouble(parts[i].Trim(), min, name);
				if (values[i] > max)
					throw new InvalidOperationException("Invalid " + name + ": " + parts[i]);
			}
			return values;
		}
	}
}
