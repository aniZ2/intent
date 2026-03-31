using System;
using System.IO;
using System.Text;

namespace Intent.StreamRunner
{
	internal sealed class RunnerLogger : IDisposable
	{
		private readonly object sync = new object();
		private readonly StreamWriter fileWriter;

		public RunnerLogger(string logFilePath)
		{
			if (!string.IsNullOrWhiteSpace(logFilePath))
			{
				string fullPath = Path.GetFullPath(logFilePath);
				string directory = Path.GetDirectoryName(fullPath);
				if (!string.IsNullOrWhiteSpace(directory))
					Directory.CreateDirectory(directory);

				fileWriter = new StreamWriter(fullPath, true, Encoding.UTF8);
				fileWriter.AutoFlush = true;
			}
		}

		public void Info(string message)
		{
			Write("info", message);
		}

		public void Error(string message)
		{
			Write("error", message);
		}

		public void Dispose()
		{
			lock (sync)
			{
				if (fileWriter != null)
					fileWriter.Dispose();
			}
		}

		private void Write(string level, string message)
		{
			string line = string.Format("{0:o} [{1}] {2}", DateTime.UtcNow, level, message);
			lock (sync)
			{
				if (string.Equals(level, "error", StringComparison.Ordinal))
					Console.Error.WriteLine(line);
				else
					Console.WriteLine(line);

				if (fileWriter != null)
					fileWriter.WriteLine(line);
			}
		}
	}
}
