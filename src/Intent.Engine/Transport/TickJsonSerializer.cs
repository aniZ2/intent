using System.Globalization;
using System.Text;
using Intent.Engine.Models;

namespace Intent.Engine.Transport
{
	public static class TickJsonSerializer
	{
		public static string ToJson(TickData tick)
		{
			StringBuilder builder = new StringBuilder(256);
			builder.Append("{");
			AppendString(builder, "timestampUtc", tick == null ? string.Empty : tick.TimestampUtc.ToString("o"), true);
			AppendString(builder, "instrument", tick == null ? string.Empty : tick.Instrument, true);
			AppendNumber(builder, "price", tick == null ? 0 : tick.Price, true);
			AppendNumber(builder, "volume", tick == null ? 0 : tick.Volume, true);
			AppendNumber(builder, "bid", tick == null ? 0 : tick.Bid, true);
			AppendNumber(builder, "ask", tick == null ? 0 : tick.Ask, true);
			AppendBoolean(builder, "isBuyerInitiated", tick != null && tick.IsBuyerInitiated, false);
			builder.Append("}");
			return builder.ToString();
		}

		private static void AppendString(StringBuilder builder, string name, string value, bool appendComma)
		{
			builder.Append("\"").Append(name).Append("\":\"").Append(Escape(value)).Append("\"");
			if (appendComma)
				builder.Append(",");
		}

		private static void AppendNumber(StringBuilder builder, string name, double value, bool appendComma)
		{
			builder.Append("\"").Append(name).Append("\":").Append(value.ToString("0.########", CultureInfo.InvariantCulture));
			if (appendComma)
				builder.Append(",");
		}

		private static void AppendNumber(StringBuilder builder, string name, long value, bool appendComma)
		{
			builder.Append("\"").Append(name).Append("\":").Append(value.ToString(CultureInfo.InvariantCulture));
			if (appendComma)
				builder.Append(",");
		}

		private static void AppendBoolean(StringBuilder builder, string name, bool value, bool appendComma)
		{
			builder.Append("\"").Append(name).Append("\":").Append(value ? "true" : "false");
			if (appendComma)
				builder.Append(",");
		}

		private static string Escape(string value)
		{
			if (string.IsNullOrEmpty(value))
				return string.Empty;

			return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
		}
	}
}
