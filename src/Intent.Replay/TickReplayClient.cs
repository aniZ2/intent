using System;
using System.IO;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;

namespace Intent.Replay
{
	internal sealed class TickReplayClient
	{
		private static readonly DataContractJsonSerializer Serializer = new DataContractJsonSerializer(typeof(ReplayTickEnvelope));
		private readonly ReplayOptions options;

		public TickReplayClient(ReplayOptions options)
		{
			this.options = options;
		}

		public void Run()
		{
			string[] lines = File.ReadAllLines(options.InputPath);
			using (TcpClient client = new TcpClient())
			{
				client.Connect(options.Host, options.Port);
				using (NetworkStream stream = client.GetStream())
				using (StreamWriter writer = new StreamWriter(stream))
				{
					writer.NewLine = "\n";
					DateTime? priorTimestampUtc = null;
					int sent = 0;

					foreach (string rawLine in lines)
					{
						string line = rawLine == null ? string.Empty : rawLine.Trim();
						if (line.Length == 0)
							continue;

						DateTime timestampUtc;
						if (TryReadTimestamp(line, out timestampUtc) && priorTimestampUtc.HasValue && options.SpeedMultiplier > 0)
						{
							TimeSpan gap = timestampUtc - priorTimestampUtc.Value;
							if (gap > TimeSpan.Zero)
							{
								int delayMilliseconds = (int)Math.Min(int.MaxValue, gap.TotalMilliseconds / options.SpeedMultiplier);
								if (delayMilliseconds > 0)
									Thread.Sleep(delayMilliseconds);
							}
						}

						writer.WriteLine(line);
						writer.Flush();
						priorTimestampUtc = timestampUtc == default(DateTime) ? priorTimestampUtc : timestampUtc;
						sent++;
					}

					Console.WriteLine("[replay] sent " + sent + " ticks to " + options.Host + ":" + options.Port);
				}
			}
		}

		private static bool TryReadTimestamp(string json, out DateTime timestampUtc)
		{
			timestampUtc = default(DateTime);

			try
			{
				ReplayTickEnvelope payload;
				using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
					payload = (ReplayTickEnvelope)Serializer.ReadObject(stream);

				if (payload == null || string.IsNullOrWhiteSpace(payload.TimestampUtc))
					return false;

				return DateTime.TryParse(payload.TimestampUtc, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out timestampUtc);
			}
			catch
			{
				return false;
			}
		}

		[DataContract]
		private sealed class ReplayTickEnvelope
		{
			[DataMember(Name = "timestampUtc", EmitDefaultValue = false)]
			public string TimestampUtc { get; set; }
		}
	}
}
