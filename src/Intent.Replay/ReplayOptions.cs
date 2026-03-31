using System;
using System.Globalization;

namespace Intent.Replay
{
	internal sealed class ReplayOptions
	{
		public ReplayOptions()
		{
			Host = "127.0.0.1";
			Port = 4100;
			SpeedMultiplier = 0;
		}

		public string InputPath { get; set; }
		public string Host { get; set; }
		public int Port { get; set; }
		public double SpeedMultiplier { get; set; }

		public static ReplayOptions Parse(string[] args)
		{
			ReplayOptions options = new ReplayOptions();
			for (int index = 0; index < (args == null ? 0 : args.Length); index++)
			{
				string argument = args[index];
				if (string.Equals(argument, "--input", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
					options.InputPath = args[++index];
				else if (string.Equals(argument, "--host", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
					options.Host = args[++index];
				else if (string.Equals(argument, "--port", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
					options.Port = ParseInt(args[++index], 1, 65535, "port");
				else if (string.Equals(argument, "--speed", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
					options.SpeedMultiplier = ParseDouble(args[++index], "speed");
				else
					throw new InvalidOperationException("Usage: Intent.Replay --input ticks.ndjson [--host 127.0.0.1] [--port 4100] [--speed 20]");
			}

			if (string.IsNullOrWhiteSpace(options.InputPath))
				throw new InvalidOperationException("Replay input file is required.");

			return options;
		}

		private static int ParseInt(string raw, int minimum, int maximum, string name)
		{
			int value;
			if (!int.TryParse(raw, out value) || value < minimum || value > maximum)
				throw new InvalidOperationException("Invalid " + name + ": " + raw);
			return value;
		}

		private static double ParseDouble(string raw, string name)
		{
			double value;
			if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value) || value < 0)
				throw new InvalidOperationException("Invalid " + name + ": " + raw);
			return value;
		}
	}
}
