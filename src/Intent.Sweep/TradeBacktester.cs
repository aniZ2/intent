using System;
using System.Collections.Generic;
using Intent.Engine.Models;

namespace Intent.Sweep
{
	internal sealed class BacktestConfig
	{
		public double TickSize = 0.25;
		public int TargetTicks = 4;
		public int StopTicks = 4;
		public int MaxHoldBars = 8;
		public double CommissionPerSide = 2.0;
		public double SlippageTicks = 1.0;
		public double TickValue = 12.5;
	}

	internal struct BacktestSignal
	{
		public int BarIndex;
		public int Direction; // +1 bullish, -1 bearish

		public BacktestSignal(int barIndex, int direction)
		{
			BarIndex = barIndex;
			Direction = direction;
		}
	}

	// Event-driven, single-position P&L simulation. It consumes the SAME engine signals used for the
	// accuracy metrics, but models REAL execution: entry at the NEXT bar's open (you cannot fill on the
	// signal bar's close), an explicit stop and target, commission + slippage, a conservative
	// stop-before-target tie-break, and a time exit. This is what reveals whether the signal stream has
	// a post-cost edge — something signal-classification F1 cannot tell you.
	internal sealed class TradeBacktester
	{
		public static BacktestResult Run(IList<BarData> bars, IList<BacktestSignal> signals, BacktestConfig config)
		{
			BacktestResult result = new BacktestResult();
			if (bars == null || signals == null || bars.Count == 0)
				return result;

			double tick = Math.Max(config.TickSize, 0.0000001);
			double slip = config.SlippageTicks * tick;
			int lastExitIndex = -1;

			for (int s = 0; s < signals.Count; s++)
			{
				BacktestSignal signal = signals[s];
				int dir = signal.Direction;
				if (dir == 0)
					continue;

				// One position at a time: ignore a signal that fires before the prior trade has closed.
				if (signal.BarIndex < lastExitIndex)
					continue;

				int entryIndex = signal.BarIndex + 1; // fill on the bar AFTER the closed signal bar
				if (entryIndex >= bars.Count)
					continue;

				double rawEntry = bars[entryIndex].Open;
				double entry = dir > 0 ? rawEntry + slip : rawEntry - slip; // adverse entry slippage
				double stop = dir > 0 ? entry - config.StopTicks * tick : entry + config.StopTicks * tick;
				double target = dir > 0 ? entry + config.TargetTicks * tick : entry - config.TargetTicks * tick;
				int lastBar = Math.Min(bars.Count - 1, entryIndex + config.MaxHoldBars);

				double exit = 0;
				int exitIndex = lastBar;
				bool resolved = false;

				for (int i = entryIndex; i <= lastBar; i++)
				{
					BarData bar = bars[i];
					bool stopHit = dir > 0 ? bar.Low <= stop : bar.High >= stop;
					bool targetHit = dir > 0 ? bar.High >= target : bar.Low <= target;

					if (stopHit)
					{
						// Conservative: if both stop and target are touched in one bar, assume the stop
						// filled first (worst case) — high/low ordering within a bar is unknown.
						exit = dir > 0 ? stop - slip : stop + slip; // market stop fill with slippage
						exitIndex = i;
						resolved = true;
						break;
					}

					if (targetHit)
					{
						exit = target; // resting limit at the target fills at the target (no slippage)
						exitIndex = i;
						resolved = true;
						break;
					}
				}

				if (!resolved)
				{
					double rawExit = bars[lastBar].Close;
					exit = dir > 0 ? rawExit - slip : rawExit + slip; // time-based market exit, slippage
					exitIndex = lastBar;
				}

				double pnlPrice = dir > 0 ? (exit - entry) : (entry - exit);
				double pnl = (pnlPrice / tick) * config.TickValue - 2.0 * config.CommissionPerSide;
				result.AddTrade(pnl);
				lastExitIndex = exitIndex;
			}

			result.Compute();
			return result;
		}
	}

	internal sealed class BacktestResult
	{
		private readonly List<double> tradePnls = new List<double>();

		public List<double> TradePnls
		{
			get { return tradePnls; }
		}

		public int Trades { get; private set; }
		public int Wins { get; private set; }
		public int Losses { get; private set; }
		public double NetPnL { get; private set; }
		public double GrossProfit { get; private set; }
		public double GrossLoss { get; private set; }
		public double Expectancy { get; private set; }
		public double WinRate { get; private set; }
		public double ProfitFactor { get; private set; }
		public double MaxDrawdown { get; private set; }
		public double SharpePerTrade { get; private set; }

		public void AddTrade(double pnl)
		{
			tradePnls.Add(pnl);
		}

		public void Compute()
		{
			Trades = tradePnls.Count;
			if (Trades == 0)
				return;

			double sum = 0;
			double grossProfit = 0;
			double grossLoss = 0;
			int wins = 0;
			int losses = 0;
			for (int i = 0; i < tradePnls.Count; i++)
			{
				double p = tradePnls[i];
				sum += p;
				if (p > 0)
				{
					grossProfit += p;
					wins++;
				}
				else if (p < 0)
				{
					grossLoss += p;
					losses++;
				}
			}

			NetPnL = sum;
			GrossProfit = grossProfit;
			GrossLoss = grossLoss;
			Wins = wins;
			Losses = losses;
			Expectancy = sum / Trades;
			WinRate = (double)wins / Trades;
			ProfitFactor = grossLoss < 0 ? grossProfit / -grossLoss : 0;

			double mean = Expectancy;
			double variance = 0;
			double equity = 0;
			double peak = 0;
			double maxDrawdown = 0;
			for (int i = 0; i < tradePnls.Count; i++)
			{
				double delta = tradePnls[i] - mean;
				variance += delta * delta;
				equity += tradePnls[i];
				if (equity > peak)
					peak = equity;
				double drawdown = peak - equity;
				if (drawdown > maxDrawdown)
					maxDrawdown = drawdown;
			}

			variance /= Trades;
			double std = Math.Sqrt(variance);
			SharpePerTrade = std > 0 ? mean / std : 0;
			MaxDrawdown = maxDrawdown;
		}

		public static BacktestResult FromTrades(List<double> pnls)
		{
			BacktestResult result = new BacktestResult();
			if (pnls != null)
				for (int i = 0; i < pnls.Count; i++)
					result.AddTrade(pnls[i]);
			result.Compute();
			return result;
		}
	}
}
