using System;
using Intent.Engine.Models;

namespace Intent.StreamRunner
{
	// Deterministic assertions for the NDJSON tick deserializer, focused on the aggressor inference that
	// used to default every quote-less / flag-less tick to the sell side (biasing delta/imbalance bearish).
	// Run via: Intent.StreamRunner --selftest.
	internal static class ConsoleSelfTest
	{
		public static int Run()
		{
			int failures = 0;
			TickJsonDeserializer deserializer = new TickJsonDeserializer();
			const string ts = "\"timestampUtc\":\"2026-03-30T14:30:00Z\"";

			failures += ExpectAggressor(deserializer, "explicit true",
				"{" + ts + ",\"price\":100.5,\"volume\":5,\"bid\":100.0,\"ask\":100.5,\"isBuyerInitiated\":true}", true, true);
			failures += ExpectAggressor(deserializer, "explicit false overrides quote",
				"{" + ts + ",\"price\":100.5,\"volume\":5,\"bid\":100.0,\"ask\":100.5,\"isBuyerInitiated\":false}", true, false);
			failures += ExpectAggressor(deserializer, "infer buyer at ask",
				"{" + ts + ",\"price\":100.5,\"volume\":5,\"bid\":100.0,\"ask\":100.5}", true, true);
			failures += ExpectAggressor(deserializer, "infer seller at bid",
				"{" + ts + ",\"price\":100.0,\"volume\":5,\"bid\":100.0,\"ask\":100.5}", true, false);
			failures += ExpectAggressor(deserializer, "infer buyer above mid",
				"{" + ts + ",\"price\":100.3,\"volume\":5,\"bid\":100.0,\"ask\":100.5}", true, true);
			failures += ExpectAggressor(deserializer, "infer seller below mid",
				"{" + ts + ",\"price\":100.2,\"volume\":5,\"bid\":100.0,\"ask\":100.5}", true, false);
			failures += ExpectAggressor(deserializer, "no quote defaults seller",
				"{" + ts + ",\"price\":100.0,\"volume\":5}", true, false);
			failures += ExpectAggressor(deserializer, "alt field buyerInitiated",
				"{" + ts + ",\"price\":100.0,\"volume\":5,\"buyerInitiated\":true}", true, true);

			failures += ExpectValid(deserializer, "missing price rejected",
				"{" + ts + ",\"volume\":5}", false);
			failures += ExpectValid(deserializer, "non-positive volume rejected",
				"{" + ts + ",\"price\":100,\"volume\":0}", false);

			Console.WriteLine(failures == 0
				? "Console self-test passed."
				: ("Console self-test FAILED: " + failures + " assertion(s)."));
			return failures == 0 ? 0 : 1;
		}

		private static int ExpectAggressor(TickJsonDeserializer deserializer, string name, string json, bool expectValid, bool expectBuyer)
		{
			TickData tick;
			string error;
			bool ok = deserializer.TryDeserialize(json, out tick, out error);
			if (ok != expectValid)
			{
				Console.Error.WriteLine("  FAIL " + name + ": valid expected " + expectValid + ", got " + ok + " (" + error + ")");
				return 1;
			}

			if (ok && tick.IsBuyerInitiated != expectBuyer)
			{
				Console.Error.WriteLine("  FAIL " + name + ": buyer expected " + expectBuyer + ", got " + tick.IsBuyerInitiated);
				return 1;
			}

			return 0;
		}

		private static int ExpectValid(TickJsonDeserializer deserializer, string name, string json, bool expectValid)
		{
			TickData tick;
			string error;
			bool ok = deserializer.TryDeserialize(json, out tick, out error);
			if (ok != expectValid)
			{
				Console.Error.WriteLine("  FAIL " + name + ": valid expected " + expectValid + ", got " + ok);
				return 1;
			}

			return 0;
		}
	}
}
