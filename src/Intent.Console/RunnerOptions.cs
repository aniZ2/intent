using System;

namespace Intent.StreamRunner
{
	internal sealed class RunnerOptions
	{
		public RunnerOptions()
		{
			Host = ReadEnv("INTENT_STREAM_HOST", "127.0.0.1");
			Port = ParseEnvInt("INTENT_STREAM_PORT", 4100);
			BarSeconds = ParseEnvInt("INTENT_STREAM_BAR_SECONDS", 60);
			TickSize = ParseEnvDouble("INTENT_STREAM_TICK_SIZE", 0.25);
			VolumeLookback = 20;
			RangeLookback = 14;
			StructureLookback = 20;
			DefaultInstrument = ReadEnv("INTENT_STREAM_INSTRUMENT", string.Empty);
			LogFilePath = ReadEnv("INTENT_STREAM_LOG_FILE", string.Empty);
			PacketOutputPath = ReadEnv("INTENT_STREAM_PACKET_FILE", string.Empty);
			TickArchiveRootPath = ReadEnv("INTENT_STREAM_TICK_ARCHIVE_DIR", string.Empty);
			DashboardPort = ParseEnvInt("INTENT_STREAM_DASHBOARD_PORT", 0);
			EmitCompletedBars = true;
			EmitSignalEvents = true;
		}

		public string Host { get; set; }
		public int Port { get; set; }
		public int BarSeconds { get; set; }
		public double TickSize { get; set; }
		public int VolumeLookback { get; set; }
		public int RangeLookback { get; set; }
		public int StructureLookback { get; set; }
		public string DefaultInstrument { get; set; }
		public string LogFilePath { get; set; }
		public string PacketOutputPath { get; set; }
		public string TickArchiveRootPath { get; set; }
		public int DashboardPort { get; set; }
		public bool EmitCompletedBars { get; set; }
		public bool EmitSignalEvents { get; set; }

		public static RunnerOptions Parse(string[] args)
		{
			RunnerOptions options = new RunnerOptions();
			for (int index = 0; index < (args == null ? 0 : args.Length); index++)
			{
				string argument = args[index];
				if (string.Equals(argument, "--port", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
					options.Port = ParseInt(args[++index], 1, 65535, "port");
				else if (string.Equals(argument, "--host", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
					options.Host = args[++index];
				else if (string.Equals(argument, "--bar-seconds", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
					options.BarSeconds = ParseInt(args[++index], 1, 86400, "bar-seconds");
				else if (string.Equals(argument, "--tick-size", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
					options.TickSize = ParseDouble(args[++index], 0.0000001, "tick-size");
				else if (string.Equals(argument, "--instrument", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
					options.DefaultInstrument = args[++index];
				else if (string.Equals(argument, "--log-file", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
					options.LogFilePath = args[++index];
				else if (string.Equals(argument, "--packet-file", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
					options.PacketOutputPath = args[++index];
				else if (string.Equals(argument, "--tick-archive-dir", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
					options.TickArchiveRootPath = args[++index];
				else if (string.Equals(argument, "--dashboard-port", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
					options.DashboardPort = ParseInt(args[++index], 0, 65535, "dashboard-port");
				else if (string.Equals(argument, "--emit-signals-only", StringComparison.OrdinalIgnoreCase))
					options.EmitCompletedBars = false;
				else if (string.Equals(argument, "--emit-bars-only", StringComparison.OrdinalIgnoreCase))
					options.EmitSignalEvents = false;
				else if (string.Equals(argument, "--help", StringComparison.OrdinalIgnoreCase) || string.Equals(argument, "-h", StringComparison.OrdinalIgnoreCase))
					throw new InvalidOperationException("Usage: Intent.StreamRunner [--host 127.0.0.1] [--port 4100] [--bar-seconds 60] [--tick-size 0.25] [--instrument ES 06-26] [--log-file logs\\\\stream.log] [--packet-file logs\\\\decisions.ndjson] [--tick-archive-dir data\\\\sessions] [--dashboard-port 4110] [--emit-signals-only|--emit-bars-only]");
				else
					throw new InvalidOperationException("Unknown argument: " + argument);
			}

			return options;
		}

		private static int ParseInt(string raw, int minimum, int maximum, string name)
		{
			int value;
			if (!int.TryParse(raw, out value) || value < minimum || value > maximum)
				throw new InvalidOperationException("Invalid " + name + ": " + raw);
			return value;
		}

		private static double ParseDouble(string raw, double minimum, string name)
		{
			double value;
			if (!double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value) || value < minimum)
				throw new InvalidOperationException("Invalid " + name + ": " + raw);
			return value;
		}

		private static string ReadEnv(string name, string fallback)
		{
			string value = Environment.GetEnvironmentVariable(name);
			return string.IsNullOrWhiteSpace(value) ? fallback : value;
		}

		private static int ParseEnvInt(string name, int fallback)
		{
			string raw = Environment.GetEnvironmentVariable(name);
			int value;
			return int.TryParse(raw, out value) && value > 0 ? value : fallback;
		}

		private static double ParseEnvDouble(string name, double fallback)
		{
			string raw = Environment.GetEnvironmentVariable(name);
			double value;
			return double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value) && value > 0
				? value
				: fallback;
		}
	}
}
