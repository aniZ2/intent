using System;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using Intent.Engine.Models;

namespace Intent.StreamRunner
{
	internal sealed class TickJsonDeserializer
	{
		private static readonly DataContractJsonSerializer Serializer = new DataContractJsonSerializer(typeof(TickWirePayload));

		public bool TryDeserialize(string json, out TickData tick, out string error)
		{
			tick = null;
			error = null;

			try
			{
				TickWirePayload payload;
				using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
					payload = (TickWirePayload)Serializer.ReadObject(stream);

				if (payload == null)
				{
					error = "Empty JSON object.";
					return false;
				}

				DateTime timestampUtc;
				if (!TryParseTimestamp(payload, out timestampUtc))
				{
					error = "Missing or invalid timestampUtc.";
					return false;
				}

				if (payload.Price == null || !IsFinite(payload.Price.Value))
				{
					error = "Missing or invalid price.";
					return false;
				}

				if (payload.Volume == null || payload.Volume.Value <= 0)
				{
					error = "Missing or invalid volume.";
					return false;
				}

				double price = payload.Price.Value;
				bool hasBid = payload.Bid != null && IsFinite(payload.Bid.Value);
				bool hasAsk = payload.Ask != null && IsFinite(payload.Ask.Value);
				double bid = hasBid ? payload.Bid.Value : price;
				double ask = hasAsk ? payload.Ask.Value : price;

				bool? explicitAggressor = payload.IsBuyerInitiated ?? payload.BuyerInitiated;
				bool isBuyerInitiated = explicitAggressor.HasValue
					? explicitAggressor.Value
					: InferBuyerInitiated(price, bid, ask, hasBid && hasAsk);

				tick = new TickData
				{
					TimestampUtc = timestampUtc.Kind == DateTimeKind.Utc ? timestampUtc : timestampUtc.ToUniversalTime(),
					Instrument = payload.Instrument ?? string.Empty,
					Price = price,
					Volume = payload.Volume.Value,
					Bid = bid,
					Ask = ask,
					IsBuyerInitiated = isBuyerInitiated
				};
				return true;
			}
			catch (Exception ex)
			{
				error = ex.Message;
				return false;
			}
		}

		private static bool TryParseTimestamp(TickWirePayload payload, out DateTime timestampUtc)
		{
			string raw = FirstNonEmpty(payload.TimestampUtc, payload.Timestamp, payload.TimeUtc);
			return DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out timestampUtc);
		}

		private static string FirstNonEmpty(string first, string second, string third)
		{
			if (!string.IsNullOrWhiteSpace(first))
				return first;
			if (!string.IsNullOrWhiteSpace(second))
				return second;
			return third ?? string.Empty;
		}

		private static bool IsFinite(double value)
		{
			return !double.IsNaN(value) && !double.IsInfinity(value);
		}

		private static bool InferBuyerInitiated(double price, double bid, double ask, bool hasQuote)
		{
			// When the feed omits an explicit aggressor flag, infer it from the quote instead of
			// defaulting every trade to the sell side (which routes all volume to BidVolume and biases
			// delta/imbalance permanently bearish). Trade at/above the ask is buyer-initiated, at/below
			// the bid is seller-initiated, in-between resolves on the mid. Without a usable quote the
			// aggressor is genuinely unknown, so fall back to seller-initiated (documented default).
			if (hasQuote && ask > bid)
			{
				if (price >= ask)
					return true;
				if (price <= bid)
					return false;
				return price >= (bid + ask) / 2.0;
			}

			return false;
		}

		[DataContract]
		private sealed class TickWirePayload
		{
			[DataMember(Name = "timestampUtc", EmitDefaultValue = false)]
			public string TimestampUtc { get; set; }

			[DataMember(Name = "timestamp", EmitDefaultValue = false)]
			public string Timestamp { get; set; }

			[DataMember(Name = "timeUtc", EmitDefaultValue = false)]
			public string TimeUtc { get; set; }

			[DataMember(Name = "instrument", EmitDefaultValue = false)]
			public string Instrument { get; set; }

			[DataMember(Name = "price", EmitDefaultValue = false)]
			public double? Price { get; set; }

			[DataMember(Name = "volume", EmitDefaultValue = false)]
			public long? Volume { get; set; }

			[DataMember(Name = "bid", EmitDefaultValue = false)]
			public double? Bid { get; set; }

			[DataMember(Name = "ask", EmitDefaultValue = false)]
			public double? Ask { get; set; }

			[DataMember(Name = "isBuyerInitiated", EmitDefaultValue = false)]
			public bool? IsBuyerInitiated { get; set; }

			[DataMember(Name = "buyerInitiated", EmitDefaultValue = false)]
			public bool? BuyerInitiated { get; set; }
		}
	}
}
