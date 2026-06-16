using System;
using System.Collections.Generic;
using Intent.Engine.Models;

namespace Intent.Sweep
{
	// Deterministic assertions for the P&L backtester arithmetic (entry-next-bar, stop/target,
	// commission + slippage, conservative tie-break, time exit). Run via: Intent.Sweep --selftest.
	internal static class SelfTest
	{
		public static int Run()
		{
			int failures = 0;
			// tick 0.25, target/stop 4 ticks, commission $2/side, slippage 1 tick, tick value $12.50.
			BacktestConfig config = new BacktestConfig
			{
				TickSize = 0.25,
				TargetTicks = 4,
				StopTicks = 4,
				MaxHoldBars = 8,
				CommissionPerSide = 2.0,
				SlippageTicks = 1.0,
				TickValue = 12.5
			};

			// 1. Long, target hit: entry 100.25 (open 100 + 1 tick slip), target 101.25; +4 ticks gross
			//    -> 4*12.5 - 2*2 = 46.
			failures += ExpectClose("long target pnl", 46.0, Single(config, 1,
				Bar(100, 100, 100, 100),
				Bar(100, 100.40, 100.00, 100.20),
				Bar(100.30, 101.50, 100.30, 101.40)).NetPnL);

			// 2. Long, stop hit: stop 99.25, market fill 99.00 (stop - 1 tick slip); -5 ticks
			//    -> -5*12.5 - 4 = -66.5.
			failures += ExpectClose("long stop pnl", -66.5, Single(config, 1,
				Bar(100, 100, 100, 100),
				Bar(100, 100.40, 100.00, 100.20),
				Bar(100.10, 100.20, 99.00, 99.10)).NetPnL);

			// 3. Short, target hit: entry 99.75 (open 100 - 1 tick slip), target 98.75; +4 ticks -> 46.
			failures += ExpectClose("short target pnl", 46.0, Single(config, -1,
				Bar(100, 100, 100, 100),
				Bar(100, 100.00, 99.60, 99.80),
				Bar(99.70, 99.70, 98.50, 98.60)).NetPnL);

			// 4. Time exit (neither stop nor target within hold): exit at last close 100.05 - slip = 99.80;
			//    entry 100.25 -> -1.8 ticks -> -1.8*12.5 - 4 = -26.5.
			failures += ExpectClose("time exit pnl", -26.5, Single(config, 1,
				Bar(100, 100, 100, 100),
				Bar(100, 100.10, 99.95, 100.05)).NetPnL);

			// 5. Conservative tie-break: when a single bar touches BOTH stop and target, assume the stop.
			failures += ExpectClose("stop-before-target tie pnl", -66.5, Single(config, 1,
				Bar(100, 100, 100, 100),
				Bar(100, 100.40, 100.00, 100.20),
				Bar(100.10, 101.50, 99.00, 100.50)).NetPnL);

			// 6. No fillable bar after the signal -> no trade.
			failures += ExpectInt("no-entry trades", 0, Single(config, 1, Bar(100, 100, 100, 100)).Trades);

			// 7. Costs make a target win net-negative when the target barely clears them: target 1 tick,
			//    +1 tick gross 12.5 minus 4 commission = +8.5 (still positive); make commission dominate.
			BacktestConfig pricey = new BacktestConfig { TickSize = 0.25, TargetTicks = 1, StopTicks = 8, MaxHoldBars = 8, CommissionPerSide = 10.0, SlippageTicks = 1.0, TickValue = 12.5 };
			// entry 100.25, target 100.50; +1 tick = 12.5 gross, minus 20 commission = -7.5 (a "winning"
			// touch that loses money after costs -- the whole point of modelling costs).
			BacktestResult costy = Single(pricey, 1,
				Bar(100, 100, 100, 100),
				Bar(100, 100.40, 100.20, 100.30),
				Bar(100.30, 100.80, 100.30, 100.70));
			failures += ExpectClose("cost-dominated touch pnl", -7.5, costy.NetPnL);

			Console.WriteLine(failures == 0
				? "Backtester self-test passed."
				: ("Backtester self-test FAILED: " + failures + " assertion(s)."));
			return failures == 0 ? 0 : 1;
		}

		private static BacktestResult Single(BacktestConfig config, int direction, params BarData[] bars)
		{
			List<BarData> barList = new List<BarData>(bars);
			List<BacktestSignal> signals = new List<BacktestSignal> { new BacktestSignal(0, direction) };
			return TradeBacktester.Run(barList, signals, config);
		}

		private static BarData Bar(double open, double high, double low, double close)
		{
			return new BarData
			{
				Open = open,
				High = high,
				Low = low,
				Close = close,
				Volume = 100,
				TickSize = 0.25,
				AverageVolume = 100,
				AverageRange = 1
			};
		}

		private static int ExpectInt(string name, int expected, int actual)
		{
			if (expected == actual)
				return 0;
			Console.Error.WriteLine("  FAIL " + name + ": expected " + expected + ", got " + actual);
			return 1;
		}

		private static int ExpectClose(string name, double expected, double actual)
		{
			if (Math.Abs(expected - actual) < 0.01)
				return 0;
			Console.Error.WriteLine("  FAIL " + name + ": expected " + expected.ToString("0.##") + ", got " + actual.ToString("0.##"));
			return 1;
		}
	}
}
