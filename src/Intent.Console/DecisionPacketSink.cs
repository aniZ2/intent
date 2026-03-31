using System;
using System.IO;
using System.Text;

namespace Intent.StreamRunner
{
	internal sealed class DecisionPacketSink : IDisposable
	{
		private readonly object sync = new object();
		private readonly StreamWriter writer;

		public DecisionPacketSink(string outputPath)
		{
			if (string.IsNullOrWhiteSpace(outputPath))
				return;

			string fullPath = Path.GetFullPath(outputPath);
			string directory = Path.GetDirectoryName(fullPath);
			if (!string.IsNullOrWhiteSpace(directory))
				Directory.CreateDirectory(directory);

			writer = new StreamWriter(fullPath, true, Encoding.UTF8);
			writer.AutoFlush = true;
		}

		public bool IsEnabled
		{
			get { return writer != null; }
		}

		public void WriteLine(string json)
		{
			if (writer == null)
				return;

			lock (sync)
				writer.WriteLine(json);
		}

		public void Dispose()
		{
			lock (sync)
			{
				if (writer != null)
					writer.Dispose();
			}
		}
	}
}
