using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Intent.StreamRunner
{
	internal sealed class RawTickArchive : IDisposable
	{
		private readonly string rootDirectory;
		private readonly Dictionary<string, StreamWriter> writers;

		public RawTickArchive(string rootDirectory)
		{
			this.rootDirectory = string.IsNullOrWhiteSpace(rootDirectory) ? string.Empty : Path.GetFullPath(rootDirectory);
			writers = new Dictionary<string, StreamWriter>(StringComparer.OrdinalIgnoreCase);
		}

		public bool IsEnabled
		{
			get { return !string.IsNullOrWhiteSpace(rootDirectory); }
		}

		public string RootDirectory
		{
			get { return rootDirectory; }
		}

		public void WriteTick(string instrument, DateTime timestampUtc, string line)
		{
			if (!IsEnabled || string.IsNullOrWhiteSpace(line))
				return;

			string safeInstrument = SanitizePathSegment(string.IsNullOrWhiteSpace(instrument) ? "unknown" : instrument);
			string fileName = timestampUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".ndjson";
			string directory = Path.Combine(rootDirectory, safeInstrument);
			Directory.CreateDirectory(directory);
			string path = Path.Combine(directory, fileName);
			StreamWriter writer = GetWriter(path);
			writer.WriteLine(line);
			writer.Flush();
		}

		public void Dispose()
		{
			foreach (KeyValuePair<string, StreamWriter> entry in writers)
			{
				try
				{
					entry.Value.Dispose();
				}
				catch
				{
				}
			}

			writers.Clear();
		}

		private StreamWriter GetWriter(string path)
		{
			StreamWriter writer;
			if (writers.TryGetValue(path, out writer))
				return writer;

			writer = new StreamWriter(path, true);
			writers[path] = writer;
			return writer;
		}

		private static string SanitizePathSegment(string value)
		{
			char[] invalid = Path.GetInvalidFileNameChars();
			char[] chars = value.ToCharArray();
			for (int index = 0; index < chars.Length; index++)
			{
				for (int invalidIndex = 0; invalidIndex < invalid.Length; invalidIndex++)
				{
					if (chars[index] == invalid[invalidIndex])
					{
						chars[index] = '_';
						break;
					}
				}
			}

			return new string(chars).Replace(' ', '_');
		}
	}
}
