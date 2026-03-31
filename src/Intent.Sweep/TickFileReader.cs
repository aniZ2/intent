using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using Intent.Engine.Models;

namespace Intent.Sweep
{
	internal static class TickFileReader
	{
		private static readonly DataContractJsonSerializer Serializer = new DataContractJsonSerializer(typeof(TickEnvelope));

		public static List<TickSession> ReadSessions(string inputPath)
		{
			List<TickSession> sessions = new List<TickSession>();
			if (Directory.Exists(inputPath))
			{
				string[] files = Directory.GetFiles(inputPath, "*.ndjson");
				Array.Sort(files, StringComparer.OrdinalIgnoreCase);
				for (int index = 0; index < files.Length; index++)
					sessions.Add(ReadSession(files[index]));
			}
			else
			{
				sessions.Add(ReadSession(inputPath));
			}

			sessions.RemoveAll(session => session.Ticks.Count == 0);
			return sessions;
		}

		private static TickSession ReadSession(string path)
		{
			TickSession session = new TickSession
			{
				Name = Path.GetFileName(path)
			};

			foreach (string rawLine in File.ReadAllLines(path))
			{
				string line = rawLine == null ? string.Empty : rawLine.Trim();
				if (line.Length == 0)
					continue;

				TickEnvelope payload;
				using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(line)))
					payload = (TickEnvelope)Serializer.ReadObject(stream);

				if (payload == null || string.IsNullOrWhiteSpace(payload.TimestampUtc))
					continue;

				DateTime timestampUtc;
				if (!DateTime.TryParse(payload.TimestampUtc, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out timestampUtc))
					continue;

				session.Ticks.Add(new TickData
				{
					TimestampUtc = timestampUtc,
					Instrument = payload.Instrument ?? string.Empty,
					Price = payload.Price,
					Volume = payload.Volume,
					Bid = payload.Bid,
					Ask = payload.Ask,
					IsBuyerInitiated = payload.IsBuyerInitiated
				});
			}

			return session;
		}

		[DataContract]
		private sealed class TickEnvelope
		{
			[DataMember(Name = "timestampUtc", EmitDefaultValue = false)]
			public string TimestampUtc { get; set; }

			[DataMember(Name = "instrument", EmitDefaultValue = false)]
			public string Instrument { get; set; }

			[DataMember(Name = "price", EmitDefaultValue = false)]
			public double Price { get; set; }

			[DataMember(Name = "volume", EmitDefaultValue = false)]
			public long Volume { get; set; }

			[DataMember(Name = "bid", EmitDefaultValue = false)]
			public double Bid { get; set; }

			[DataMember(Name = "ask", EmitDefaultValue = false)]
			public double Ask { get; set; }

			[DataMember(Name = "isBuyerInitiated", EmitDefaultValue = false)]
			public bool IsBuyerInitiated { get; set; }
		}
	}

	internal sealed class TickSession
	{
		public TickSession()
		{
			Ticks = new List<TickData>();
		}

		public string Name { get; set; }
		public List<TickData> Ticks { get; private set; }
	}
}
