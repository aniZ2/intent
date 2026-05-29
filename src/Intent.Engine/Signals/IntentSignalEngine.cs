using System;
using System.Collections.Generic;
using Intent.Engine.Ingestion;
using Intent.Engine.Models;

namespace Intent.Engine.Signals
{
	public sealed class IntentSignalEngine
	{
		public SignalResult Analyze(BarData bar, EngineSettings settings)
		{
			SignalResult result = new SignalResult();
			result.Bar = bar;

			EvaluateImbalance(bar, settings, result.Imbalance);
			EvaluateAbsorption(bar, settings, result.Absorption);
			EvaluateFailedBreakout(bar, settings, result.FailedBreakout);
			EvaluateLiquiditySweep(bar, settings, result.LiquiditySweep);
			EvaluateBreakoutContinuation(bar, settings, result.BreakoutContinuation);
			FinalizeScores(bar, settings, result);

			return result;
		}

		private static void EvaluateImbalance(BarData bar, EngineSettings settings, SignalScore score)
		{
			if (bar.OrderFlow != null && bar.OrderFlow.IsAvailable)
			{
				SignalFactor[] bullishFactors = new[]
				{
					CreateFactor("Ask imbalance levels", bar.OrderFlow.AskImbalanceLevels, NormalizeAbove(bar.OrderFlow.AskImbalanceLevels, 1.0, settings.ImbalanceLevelNormalizationSpan), 35, "Stacked ask-side imbalance."),
					CreateFactor("Ask imbalance ratio", bar.OrderFlow.AskImbalanceRatio, NormalizeAbove(bar.OrderFlow.AskImbalanceRatio, settings.ImbalanceRatioThreshold, settings.ImbalanceRatioNormalizationSpan), 25, "Strong ask-over-bid ratio."),
					CreateFactor("Delta per volume", bar.OrderFlow.DeltaPerVolume, bar.OrderFlow.BarDelta > 0 ? NormalizeAbove(bar.OrderFlow.DeltaPerVolume, settings.DeltaPerVolumeBaseline, settings.DeltaPerVolumeNormalizationSpan) : 0, 20, "Positive delta supported by volume."),
					CreateFactor("Close location", bar.CloseLocation, NormalizeAbove(bar.CloseLocation, 0.50, settings.CloseLocationNormalizationSpan), 20, "Close holding near the bar high.")
				};

				SignalFactor[] bearishFactors = new[]
				{
					CreateFactor("Bid imbalance levels", bar.OrderFlow.BidImbalanceLevels, NormalizeAbove(bar.OrderFlow.BidImbalanceLevels, 1.0, settings.ImbalanceLevelNormalizationSpan), 35, "Stacked bid-side imbalance."),
					CreateFactor("Bid imbalance ratio", bar.OrderFlow.BidImbalanceRatio, NormalizeAbove(bar.OrderFlow.BidImbalanceRatio, settings.ImbalanceRatioThreshold, settings.ImbalanceRatioNormalizationSpan), 25, "Strong bid-over-ask ratio."),
					CreateFactor("Delta per volume", bar.OrderFlow.DeltaPerVolume, bar.OrderFlow.BarDelta < 0 ? NormalizeAbove(bar.OrderFlow.DeltaPerVolume, settings.DeltaPerVolumeBaseline, settings.DeltaPerVolumeNormalizationSpan) : 0, 20, "Negative delta supported by volume."),
					CreateFactor("Close location", bar.CloseLocation, NormalizeBelow(bar.CloseLocation, 0.50, settings.CloseLocationNormalizationSpan), 20, "Close holding near the bar low.")
				};

				double bullish = SumContributions(bullishFactors);
				double bearish = SumContributions(bearishFactors);

				if (bar.OrderFlow.BarDelta <= 0)
				{
					double penalty = ContradictionPenalty(Math.Abs(bar.OrderFlow.DeltaPerVolume), settings);
					bullish *= penalty;
					AppendAdjustedFactor(ref bullishFactors, "Delta direction penalty", bar.OrderFlow.BarDelta, penalty, bullish, "Bullish imbalance penalized by contradicting delta magnitude.");
				}

				if (bar.OrderFlow.BarDelta >= 0)
				{
					double penalty = ContradictionPenalty(Math.Abs(bar.OrderFlow.DeltaPerVolume), settings);
					bearish *= penalty;
					AppendAdjustedFactor(ref bearishFactors, "Delta direction penalty", bar.OrderFlow.BarDelta, penalty, bearish, "Bearish imbalance penalized by contradicting delta magnitude.");
				}

				score.SetScores(bullish, bearish, "Ask-side imbalance stacked with positive delta", "Bid-side imbalance stacked with negative delta", bullishFactors, bearishFactors);
				return;
			}

			SignalFactor[] fallbackBullishFactors = new[]
			{
				CreateFactor("Close location", bar.CloseLocation, NormalizeAbove(bar.CloseLocation, 0.55, settings.FallbackCloseLocationNormalizationSpan), 40, "Close holding near the bar high."),
				CreateFactor("Body ratio", bar.BodyRatio, NormalizeAbove(bar.BodyRatio, settings.BodyRatioBaseline, settings.BodyRatioNormalizationSpan), 35, "Directional body expansion."),
				CreateFactor("Volume spike", bar.VolumeSpike, NormalizeAbove(bar.VolumeSpike, settings.ImbalanceVolumeSpikeThreshold, settings.VolumeSpikeNormalizationSpan), 25, "Elevated participation without volumetric data.")
			};

			SignalFactor[] fallbackBearishFactors = new[]
			{
				CreateFactor("Close location", bar.CloseLocation, NormalizeBelow(bar.CloseLocation, 0.45, settings.FallbackCloseLocationNormalizationSpan), 40, "Close holding near the bar low."),
				CreateFactor("Body ratio", bar.BodyRatio, NormalizeAbove(bar.BodyRatio, settings.BodyRatioBaseline, settings.BodyRatioNormalizationSpan), 35, "Directional body expansion."),
				CreateFactor("Volume spike", bar.VolumeSpike, NormalizeAbove(bar.VolumeSpike, settings.ImbalanceVolumeSpikeThreshold, settings.VolumeSpikeNormalizationSpan), 25, "Elevated participation without volumetric data.")
			};

			double fallbackBullish = SumContributions(fallbackBullishFactors);
			double fallbackBearish = SumContributions(fallbackBearishFactors);

			if (bar.Body <= 0)
			{
				fallbackBullish *= 0.35;
				AppendAdjustedFactor(ref fallbackBullishFactors, "Body direction penalty", bar.Body, 0.35, fallbackBullish, "Bullish imbalance penalized by non-positive body.");
			}

			if (bar.Body >= 0)
			{
				fallbackBearish *= 0.35;
				AppendAdjustedFactor(ref fallbackBearishFactors, "Body direction penalty", bar.Body, 0.35, fallbackBearish, "Bearish imbalance penalized by non-negative body.");
			}

			score.SetScores(fallbackBullish, fallbackBearish, "Directional close + large body + elevated volume", "Directional close + large body + elevated volume", fallbackBullishFactors, fallbackBearishFactors);
		}

		private static void EvaluateAbsorption(BarData bar, EngineSettings settings, SignalScore score)
		{
			if (bar.OrderFlow != null && bar.OrderFlow.IsAvailable)
			{
				SignalFactor[] bullishFactors = new[]
				{
					CreateFactor("Opposing delta", bar.OrderFlow.BarDelta, bar.OrderFlow.BarDelta < 0 ? NormalizeAbove(Math.Abs(bar.OrderFlow.DeltaPerVolume), settings.AbsorptionDeltaThresholdRatio, settings.DeltaPerVolumeNormalizationSpan) : 0, 30, "Selling pressure was absorbed."),
					CreateFactor("Delta per volume", bar.OrderFlow.DeltaPerVolume, bar.OrderFlow.BarDelta < 0 ? NormalizeAbove(bar.OrderFlow.DeltaPerVolume, settings.AbsorptionDeltaThresholdRatio, settings.DeltaPerVolumeNormalizationSpan) : 0, 35, "Delta was large relative to volume."),
					CreateFactor("Price efficiency", bar.PriceEfficiency, NormalizeBelow(bar.PriceEfficiency, settings.AbsorptionPriceEfficiencyThreshold, settings.AbsorptionPriceEfficiencyThreshold), 20, "Price barely moved despite pressure."),
					CreateFactor("Close location", bar.CloseLocation, NormalizeAbove(bar.CloseLocation, 0.55, settings.FallbackCloseLocationNormalizationSpan), 15, "Close held away from the sell pressure.")
				};

				SignalFactor[] bearishFactors = new[]
				{
					CreateFactor("Opposing delta", bar.OrderFlow.BarDelta, bar.OrderFlow.BarDelta > 0 ? NormalizeAbove(Math.Abs(bar.OrderFlow.DeltaPerVolume), settings.AbsorptionDeltaThresholdRatio, settings.DeltaPerVolumeNormalizationSpan) : 0, 30, "Buying pressure was absorbed."),
					CreateFactor("Delta per volume", bar.OrderFlow.DeltaPerVolume, bar.OrderFlow.BarDelta > 0 ? NormalizeAbove(bar.OrderFlow.DeltaPerVolume, settings.AbsorptionDeltaThresholdRatio, settings.DeltaPerVolumeNormalizationSpan) : 0, 35, "Delta was large relative to volume."),
					CreateFactor("Price efficiency", bar.PriceEfficiency, NormalizeBelow(bar.PriceEfficiency, settings.AbsorptionPriceEfficiencyThreshold, settings.AbsorptionPriceEfficiencyThreshold), 20, "Price barely moved despite pressure."),
					CreateFactor("Close location", bar.CloseLocation, NormalizeBelow(bar.CloseLocation, 0.45, settings.FallbackCloseLocationNormalizationSpan), 15, "Close held away from the buy pressure.")
				};

				double bullish = SumContributions(bullishFactors);
				double bearish = SumContributions(bearishFactors);

				if (bar.RangeExpansion > settings.RangeExpansionPenaltyThreshold)
				{
					bullish *= 0.75;
					bearish *= 0.75;
					AppendAdjustedFactor(ref bullishFactors, "Expansion penalty", bar.RangeExpansion, 0.75, bullish, "Absorption penalized by outsized range expansion.");
					AppendAdjustedFactor(ref bearishFactors, "Expansion penalty", bar.RangeExpansion, 0.75, bearish, "Absorption penalized by outsized range expansion.");
				}

				score.SetScores(bullish, bearish, "Heavy selling delta absorbed with limited downward progress", "Heavy buying delta absorbed with limited upward progress", bullishFactors, bearishFactors);
				return;
			}

			SignalFactor[] fallbackBullishFactors = new[]
			{
				CreateFactor("Lower wick ratio", bar.LowerWickRatio, NormalizeAbove(bar.LowerWickRatio, settings.AbsorptionWickThreshold, settings.AbsorptionWickNormalizationSpan), 35, "Tail rejection at the lows."),
				CreateFactor("Close location", bar.CloseLocation, NormalizeAbove(bar.CloseLocation, 0.55, settings.FallbackCloseLocationNormalizationSpan), 25, "Close held in the upper half."),
				CreateFactor("Volume spike", bar.VolumeSpike, NormalizeAbove(bar.VolumeSpike, settings.AbsorptionVolumeSpikeThreshold, settings.VolumeSpikeNormalizationSpan), 25, "Participation expanded during rejection."),
				CreateFactor("Range expansion", bar.RangeExpansion, NormalizeAbove(bar.RangeExpansion, settings.RangeExpansionNormalizationBaseline, settings.RangeExpansionNormalizationSpan), 15, "Range was meaningful enough to matter.")
			};

			SignalFactor[] fallbackBearishFactors = new[]
			{
				CreateFactor("Upper wick ratio", bar.UpperWickRatio, NormalizeAbove(bar.UpperWickRatio, settings.AbsorptionWickThreshold, settings.AbsorptionWickNormalizationSpan), 35, "Tail rejection at the highs."),
				CreateFactor("Close location", bar.CloseLocation, NormalizeBelow(bar.CloseLocation, 0.45, settings.FallbackCloseLocationNormalizationSpan), 25, "Close held in the lower half."),
				CreateFactor("Volume spike", bar.VolumeSpike, NormalizeAbove(bar.VolumeSpike, settings.AbsorptionVolumeSpikeThreshold, settings.VolumeSpikeNormalizationSpan), 25, "Participation expanded during rejection."),
				CreateFactor("Range expansion", bar.RangeExpansion, NormalizeAbove(bar.RangeExpansion, settings.RangeExpansionNormalizationBaseline, settings.RangeExpansionNormalizationSpan), 15, "Range was meaningful enough to matter.")
			};

			double fallbackBullish = SumContributions(fallbackBullishFactors);
			double fallbackBearish = SumContributions(fallbackBearishFactors);

			if (!bar.IsBullishBody)
			{
				fallbackBullish *= 0.8;
				AppendAdjustedFactor(ref fallbackBullishFactors, "Body confirmation penalty", bar.Body, 0.8, fallbackBullish, "Bullish absorption penalized by non-bullish body.");
			}

			if (!bar.IsBearishBody)
			{
				fallbackBearish *= 0.8;
				AppendAdjustedFactor(ref fallbackBearishFactors, "Body confirmation penalty", bar.Body, 0.8, fallbackBearish, "Bearish absorption penalized by non-bearish body.");
			}

			score.SetScores(fallbackBullish, fallbackBearish, "Lower-tail rejection with heavy volume", "Upper-tail rejection with heavy volume", fallbackBullishFactors, fallbackBearishFactors);
		}

		private static void EvaluateFailedBreakout(BarData bar, EngineSettings settings, SignalScore score)
		{
			double bearishZoneConfirmation = PriceLevelConfirmation(bar, settings, true);
			double bullishZoneConfirmation = PriceLevelConfirmation(bar, settings, false);

			SignalFactor[] bearishFactors = new[]
			{
				CreateFactor("Break above ticks", bar.BreakAboveTicks, NormalizeAbove(bar.BreakAboveTicks, settings.BreakoutExcursionTicks, settings.BreakoutNormalizationSpan), 35, "Price extended beyond the prior high."),
				CreateFactor("Reclaim below high", bar.ReclaimBelowHighTicks, NormalizeAbove(bar.ReclaimBelowHighTicks, settings.ReclaimTicks, settings.BreakoutNormalizationSpan), 25, "Breakout failed to hold above the prior high."),
				CreateFactor("Close location", bar.CloseLocation, NormalizeBelow(bar.CloseLocation, settings.ReversalCloseLocationThreshold, settings.FallbackCloseLocationNormalizationSpan), 15, "Close rotated back toward the lows."),
				CreateDirectionalFactor("Bar delta confirmation", bar.OrderFlow != null ? bar.OrderFlow.BarDelta : 0, bar.OrderFlow != null && bar.OrderFlow.IsAvailable && bar.OrderFlow.BarDelta < 0, 10, "Order flow confirmed the bearish trap."),
				CreateFactor("Bid imbalance levels", bar.OrderFlow != null ? bar.OrderFlow.BidImbalanceLevels : 0, bar.OrderFlow != null && bar.OrderFlow.IsAvailable ? NormalizeAbove(bar.OrderFlow.BidImbalanceLevels, 1.0, settings.ImbalanceLevelNormalizationSpan) : 0, 10, "Bid imbalance expanded on the reversal."),
				CreateFactor("Breakout zone confirmation", bearishZoneConfirmation, bearishZoneConfirmation, 15, "Negative delta accumulated above the failed breakout level.")
			};

			SignalFactor[] bullishFactors = new[]
			{
				CreateFactor("Break below ticks", bar.BreakBelowTicks, NormalizeAbove(bar.BreakBelowTicks, settings.BreakoutExcursionTicks, settings.BreakoutNormalizationSpan), 35, "Price extended beyond the prior low."),
				CreateFactor("Reclaim above low", bar.ReclaimAboveLowTicks, NormalizeAbove(bar.ReclaimAboveLowTicks, settings.ReclaimTicks, settings.BreakoutNormalizationSpan), 25, "Breakout failed to hold below the prior low."),
				CreateFactor("Close location", bar.CloseLocation, NormalizeAbove(bar.CloseLocation, 1.0 - settings.ReversalCloseLocationThreshold, settings.FallbackCloseLocationNormalizationSpan), 15, "Close rotated back toward the highs."),
				CreateDirectionalFactor("Bar delta confirmation", bar.OrderFlow != null ? bar.OrderFlow.BarDelta : 0, bar.OrderFlow != null && bar.OrderFlow.IsAvailable && bar.OrderFlow.BarDelta > 0, 10, "Order flow confirmed the bullish trap."),
				CreateFactor("Ask imbalance levels", bar.OrderFlow != null ? bar.OrderFlow.AskImbalanceLevels : 0, bar.OrderFlow != null && bar.OrderFlow.IsAvailable ? NormalizeAbove(bar.OrderFlow.AskImbalanceLevels, 1.0, settings.ImbalanceLevelNormalizationSpan) : 0, 10, "Ask imbalance expanded on the reversal."),
				CreateFactor("Breakout zone confirmation", bullishZoneConfirmation, bullishZoneConfirmation, 15, "Positive delta accumulated below the failed breakout level.")
			};

			double bullish = SumContributions(bullishFactors);
			double bearish = SumContributions(bearishFactors);

			if (bar.BreakBelowTicks < settings.BreakoutExcursionTicks)
			{
				bullish = 0;
				AppendAdjustedFactor(ref bullishFactors, "Breakout gate", bar.BreakBelowTicks, 0, 0, "No genuine break below the prior low; failed-breakout suppressed.");
			}

			if (bar.BreakAboveTicks < settings.BreakoutExcursionTicks)
			{
				bearish = 0;
				AppendAdjustedFactor(ref bearishFactors, "Breakout gate", bar.BreakAboveTicks, 0, 0, "No genuine break above the prior high; failed-breakout suppressed.");
			}

			score.SetScores(bullish, bearish, "Break below prior low failed and reclaimed", "Break above prior high failed and reclaimed", bullishFactors, bearishFactors);
		}

		private static void EvaluateLiquiditySweep(BarData bar, EngineSettings settings, SignalScore score)
		{
			double bearishZoneConfirmation = PriceLevelConfirmation(bar, settings, true);
			double bullishZoneConfirmation = PriceLevelConfirmation(bar, settings, false);

			SignalFactor[] bearishFactors = new[]
			{
				CreateFactor("Break above ticks", bar.BreakAboveTicks, NormalizeAbove(bar.BreakAboveTicks, settings.BreakoutExcursionTicks, settings.BreakoutNormalizationSpan), 30, "Price swept above the prior high."),
				CreateFactor("Upper wick ratio", bar.UpperWickRatio, NormalizeAbove(bar.UpperWickRatio, settings.SweepWickThreshold, settings.SweepWickNormalizationSpan), 35, "Upper wick shows rejection."),
				CreateFactor("Volume spike", bar.VolumeSpike, NormalizeAbove(bar.VolumeSpike, settings.SweepVolumeSpikeThreshold, settings.SweepVolumeNormalizationSpan), 20, "Participation expanded into the sweep."),
				CreateFactor("Reclaim below high", bar.ReclaimBelowHighTicks, NormalizeAbove(bar.ReclaimBelowHighTicks, settings.ReclaimTicks, settings.BreakoutNormalizationSpan), 15, "Close snapped back under the prior high."),
				CreateFactor("Breakout zone confirmation", bearishZoneConfirmation, bearishZoneConfirmation, 10, "Negative delta appeared in the swept zone.")
			};

			SignalFactor[] bullishFactors = new[]
			{
				CreateFactor("Break below ticks", bar.BreakBelowTicks, NormalizeAbove(bar.BreakBelowTicks, settings.BreakoutExcursionTicks, settings.BreakoutNormalizationSpan), 30, "Price swept below the prior low."),
				CreateFactor("Lower wick ratio", bar.LowerWickRatio, NormalizeAbove(bar.LowerWickRatio, settings.SweepWickThreshold, settings.SweepWickNormalizationSpan), 35, "Lower wick shows rejection."),
				CreateFactor("Volume spike", bar.VolumeSpike, NormalizeAbove(bar.VolumeSpike, settings.SweepVolumeSpikeThreshold, settings.SweepVolumeNormalizationSpan), 20, "Participation expanded into the sweep."),
				CreateFactor("Reclaim above low", bar.ReclaimAboveLowTicks, NormalizeAbove(bar.ReclaimAboveLowTicks, settings.ReclaimTicks, settings.BreakoutNormalizationSpan), 15, "Close snapped back over the prior low."),
				CreateFactor("Breakout zone confirmation", bullishZoneConfirmation, bullishZoneConfirmation, 10, "Positive delta appeared in the swept zone.")
			};

			double bullish = SumContributions(bullishFactors);
			double bearish = SumContributions(bearishFactors);

			if (bar.BreakBelowTicks < settings.BreakoutExcursionTicks)
			{
				bullish = 0;
				AppendAdjustedFactor(ref bullishFactors, "Sweep gate", bar.BreakBelowTicks, 0, 0, "No genuine sweep below the prior low; sweep suppressed.");
			}

			if (bar.BreakAboveTicks < settings.BreakoutExcursionTicks)
			{
				bearish = 0;
				AppendAdjustedFactor(ref bearishFactors, "Sweep gate", bar.BreakAboveTicks, 0, 0, "No genuine sweep above the prior high; sweep suppressed.");
			}

			score.SetScores(bullish, bearish, "Sell-side sweep and fast reclaim", "Buy-side sweep and fast reclaim", bullishFactors, bearishFactors);
		}

		private static void EvaluateBreakoutContinuation(BarData bar, EngineSettings settings, SignalScore score)
		{
			double bullishZoneContinuation = PriceLevelContinuation(bar, settings, false);
			double bearishZoneContinuation = PriceLevelContinuation(bar, settings, true);
			double closeThroughTicksAbove = bar.Close > bar.PriorSwingHigh ? (bar.Close - bar.PriorSwingHigh) / Math.Max(bar.TickSize, 0.0000001) : 0;
			double closeThroughTicksBelow = bar.Close < bar.PriorSwingLow ? (bar.PriorSwingLow - bar.Close) / Math.Max(bar.TickSize, 0.0000001) : 0;

			SignalFactor[] bullishFactors = new[]
			{
				CreateFactor("Break above ticks", bar.BreakAboveTicks, NormalizeAbove(bar.BreakAboveTicks, settings.BreakoutExcursionTicks, settings.BreakoutNormalizationSpan), 25, "Price expanded above the prior high."),
				CreateFactor("Close above level", closeThroughTicksAbove, NormalizeAbove(closeThroughTicksAbove, settings.BreakoutCloseThroughLevelTicks, settings.BreakoutNormalizationSpan), 25, "Close held above the prior high."),
				CreateFactor("Ask imbalance continuation", bar.OrderFlow != null ? bar.OrderFlow.AskImbalanceLevels : 0, bar.OrderFlow != null && bar.OrderFlow.IsAvailable ? NormalizeAbove(bar.OrderFlow.AskImbalanceLevels, 1.0, settings.ImbalanceLevelNormalizationSpan) : 0, 15, "Aggressive buyers stayed active through the break."),
				CreateFactor("Volume spike", bar.VolumeSpike, NormalizeAbove(bar.VolumeSpike, settings.BreakoutVolumeSpikeThreshold, settings.VolumeSpikeNormalizationSpan), 15, "Participation expanded on the break."),
				CreateFactor("Zone continuation", bullishZoneContinuation, bullishZoneContinuation, 20, "Positive delta held beyond the breakout level.")
			};

			SignalFactor[] bearishFactors = new[]
			{
				CreateFactor("Break below ticks", bar.BreakBelowTicks, NormalizeAbove(bar.BreakBelowTicks, settings.BreakoutExcursionTicks, settings.BreakoutNormalizationSpan), 25, "Price expanded below the prior low."),
				CreateFactor("Close below level", closeThroughTicksBelow, NormalizeAbove(closeThroughTicksBelow, settings.BreakoutCloseThroughLevelTicks, settings.BreakoutNormalizationSpan), 25, "Close held below the prior low."),
				CreateFactor("Bid imbalance continuation", bar.OrderFlow != null ? bar.OrderFlow.BidImbalanceLevels : 0, bar.OrderFlow != null && bar.OrderFlow.IsAvailable ? NormalizeAbove(bar.OrderFlow.BidImbalanceLevels, 1.0, settings.ImbalanceLevelNormalizationSpan) : 0, 15, "Aggressive sellers stayed active through the break."),
				CreateFactor("Volume spike", bar.VolumeSpike, NormalizeAbove(bar.VolumeSpike, settings.BreakoutVolumeSpikeThreshold, settings.VolumeSpikeNormalizationSpan), 15, "Participation expanded on the break."),
				CreateFactor("Zone continuation", bearishZoneContinuation, bearishZoneContinuation, 20, "Negative delta held beyond the breakout level.")
			};

			double bullish = SumContributions(bullishFactors);
			double bearish = SumContributions(bearishFactors);

			if (bar.ReclaimBelowHighTicks >= settings.ReclaimTicks)
			{
				bullish *= 0.45;
				AppendAdjustedFactor(ref bullishFactors, "Failure penalty", bar.ReclaimBelowHighTicks, 0.45, bullish, "Bullish continuation penalized because price reclaimed back below the breakout level.");
			}

			if (bar.ReclaimAboveLowTicks >= settings.ReclaimTicks)
			{
				bearish *= 0.45;
				AppendAdjustedFactor(ref bearishFactors, "Failure penalty", bar.ReclaimAboveLowTicks, 0.45, bearish, "Bearish continuation penalized because price reclaimed back above the breakout level.");
			}

			score.SetScores(bullish, bearish, "Break above prior high held and continued", "Break below prior low held and continued", bullishFactors, bearishFactors);
		}

		private static double PriceLevelConfirmation(BarData bar, EngineSettings settings, bool aboveBreakout)
		{
			if (bar.OrderFlow == null || bar.OrderFlow.PriceLevels == null || bar.OrderFlow.PriceLevels.Count == 0)
				return 0;

			double breakoutPrice = aboveBreakout ? bar.PriorSwingHigh : bar.PriorSwingLow;
			long directionalDelta = 0;
			long directionalVolume = 0;
			List<OrderFlowPriceLevel> orderedLevels = new List<OrderFlowPriceLevel>(bar.OrderFlow.PriceLevels);
			orderedLevels.Sort((left, right) => left.Price.CompareTo(right.Price));

			foreach (OrderFlowPriceLevel level in orderedLevels)
			{
				if (aboveBreakout && level.Price >= breakoutPrice)
				{
					directionalDelta += level.Delta;
					directionalVolume += level.TotalVolume;
				}
				else if (!aboveBreakout && level.Price <= breakoutPrice)
				{
					directionalDelta += level.Delta;
					directionalVolume += level.TotalVolume;
				}
			}

			if (directionalVolume <= 0)
				return 0;

			double normalized = SignalMath.SafeRatio(Math.Abs(directionalDelta), directionalVolume);
			bool confirming = aboveBreakout ? directionalDelta < 0 : directionalDelta > 0;
			return confirming ? NormalizeAbove(normalized, settings.BreakoutZoneDeltaBaseline, settings.BreakoutZoneDeltaNormalizationSpan) : 0;
		}

		private static double PriceLevelContinuation(BarData bar, EngineSettings settings, bool belowBreakout)
		{
			if (bar.OrderFlow == null || bar.OrderFlow.PriceLevels == null || bar.OrderFlow.PriceLevels.Count == 0)
				return 0;

			double breakoutPrice = belowBreakout ? bar.PriorSwingLow : bar.PriorSwingHigh;
			long directionalDelta = 0;
			long directionalVolume = 0;
			List<OrderFlowPriceLevel> orderedLevels = new List<OrderFlowPriceLevel>(bar.OrderFlow.PriceLevels);
			orderedLevels.Sort((left, right) => left.Price.CompareTo(right.Price));

			foreach (OrderFlowPriceLevel level in orderedLevels)
			{
				if (belowBreakout && level.Price <= breakoutPrice)
				{
					directionalDelta += level.Delta;
					directionalVolume += level.TotalVolume;
				}
				else if (!belowBreakout && level.Price >= breakoutPrice)
				{
					directionalDelta += level.Delta;
					directionalVolume += level.TotalVolume;
				}
			}

			if (directionalVolume <= 0)
				return 0;

			double normalized = SignalMath.SafeRatio(Math.Abs(directionalDelta), directionalVolume);
			bool confirming = belowBreakout ? directionalDelta < 0 : directionalDelta > 0;
			return confirming ? NormalizeAbove(normalized, settings.BreakoutZoneDeltaBaseline, settings.BreakoutZoneDeltaNormalizationSpan) : 0;
		}

		private static void FinalizeScores(BarData bar, EngineSettings settings, SignalResult result)
		{
			ApplyContradictorySignalSuppression(result, settings);

			double totalWeight = settings.ImbalanceWeight + settings.AbsorptionWeight + settings.FailedBreakoutWeight + settings.LiquiditySweepWeight + settings.BreakoutContinuationWeight;
			if (totalWeight <= 0)
				totalWeight = 1.0;

			SignalFactor[] bullishScoreFactors = new[]
			{
				CreateFactor("Imbalance weighted", result.Imbalance.Bullish, result.Imbalance.Bullish / 100.0, settings.ImbalanceWeight / totalWeight * 100.0, "Weighted imbalance contribution."),
				CreateFactor("Absorption weighted", result.Absorption.Bullish, result.Absorption.Bullish / 100.0, settings.AbsorptionWeight / totalWeight * 100.0, "Weighted absorption contribution."),
				CreateFactor("Failed breakout weighted", result.FailedBreakout.Bullish, result.FailedBreakout.Bullish / 100.0, settings.FailedBreakoutWeight / totalWeight * 100.0, "Weighted failed-breakout contribution."),
				CreateFactor("Liquidity sweep weighted", result.LiquiditySweep.Bullish, result.LiquiditySweep.Bullish / 100.0, settings.LiquiditySweepWeight / totalWeight * 100.0, "Weighted sweep contribution."),
				CreateFactor("Breakout continuation weighted", result.BreakoutContinuation.Bullish, result.BreakoutContinuation.Bullish / 100.0, settings.BreakoutContinuationWeight / totalWeight * 100.0, "Weighted breakout-continuation contribution.")
			};

			SignalFactor[] bearishScoreFactors = new[]
			{
				CreateFactor("Imbalance weighted", result.Imbalance.Bearish, result.Imbalance.Bearish / 100.0, settings.ImbalanceWeight / totalWeight * 100.0, "Weighted imbalance contribution."),
				CreateFactor("Absorption weighted", result.Absorption.Bearish, result.Absorption.Bearish / 100.0, settings.AbsorptionWeight / totalWeight * 100.0, "Weighted absorption contribution."),
				CreateFactor("Failed breakout weighted", result.FailedBreakout.Bearish, result.FailedBreakout.Bearish / 100.0, settings.FailedBreakoutWeight / totalWeight * 100.0, "Weighted failed-breakout contribution."),
				CreateFactor("Liquidity sweep weighted", result.LiquiditySweep.Bearish, result.LiquiditySweep.Bearish / 100.0, settings.LiquiditySweepWeight / totalWeight * 100.0, "Weighted sweep contribution."),
				CreateFactor("Breakout continuation weighted", result.BreakoutContinuation.Bearish, result.BreakoutContinuation.Bearish / 100.0, settings.BreakoutContinuationWeight / totalWeight * 100.0, "Weighted breakout-continuation contribution.")
			};

			double bullScore = SumContributions(bullishScoreFactors);
			double bearScore = SumContributions(bearishScoreFactors);

			if (CountStrongSignals(result, IntentDirection.Bullish, settings.SignalThreshold) >= 2)
			{
				bullScore += settings.ConfluenceBonus;
				AppendFactor(ref bullishScoreFactors, CreateFactor("Confluence bonus", settings.ConfluenceBonus, 1.0, 1.0, "Multiple bullish signals aligned.", settings.ConfluenceBonus));
			}

			if (CountStrongSignals(result, IntentDirection.Bearish, settings.SignalThreshold) >= 2)
			{
				bearScore += settings.ConfluenceBonus;
				AppendFactor(ref bearishScoreFactors, CreateFactor("Confluence bonus", settings.ConfluenceBonus, 1.0, 1.0, "Multiple bearish signals aligned.", settings.ConfluenceBonus));
			}

			if (bar.VolumeSpike >= settings.SweepVolumeSpikeThreshold && bar.RangeExpansion >= settings.ExpansiveVolumeRangeExpansionThreshold)
			{
				bullScore += settings.ExpansiveVolumeBonus;
				bearScore += settings.ExpansiveVolumeBonus;
				AppendFactor(ref bullishScoreFactors, CreateFactor("Expansive volume bonus", bar.VolumeSpike, 1.0, 1.0, "Wide, high-volume bar added context.", settings.ExpansiveVolumeBonus));
				AppendFactor(ref bearishScoreFactors, CreateFactor("Expansive volume bonus", bar.VolumeSpike, 1.0, 1.0, "Wide, high-volume bar added context.", settings.ExpansiveVolumeBonus));
			}

			result.BullishScoreFactors = bullishScoreFactors;
			result.BearishScoreFactors = bearishScoreFactors;
			result.BullScore = SignalMath.Clamp100(bullScore);
			result.BearScore = SignalMath.Clamp100(bearScore);
			ApplyPriorSignalContext(bar, settings, result, ref bullishScoreFactors, ref bearishScoreFactors);
			result.BullishScoreFactors = bullishScoreFactors;
			result.BearishScoreFactors = bearishScoreFactors;
			result.IntentScore = Math.Max(result.BullScore, result.BearScore);

			if (Math.Abs(result.BullScore - result.BearScore) < settings.NeutralityBuffer || result.IntentScore < settings.SignalThreshold)
			{
				result.TrendDirection = DetermineTrendDirection(bar, settings);
				result.SignalClassification = IntentSignalClassification.Neutral;
				result.Direction = IntentDirection.Neutral;
				result.RecommendedTradeAction = TradeAction.StandAside;
				result.EntryStyle = "None";
				result.StopLevel = string.Empty;
				result.DominantReason = "No dominant intent";
				return;
			}

			result.Direction = result.BullScore > result.BearScore ? IntentDirection.Bullish : IntentDirection.Bearish;
			result.TrendDirection = DetermineTrendDirection(bar, settings);
			result.SignalClassification = ClassifySignal(bar, settings, result);
			ApplyTradeRules(bar, settings, result);
			result.DominantReason = result.GetDominantSignal(result.Direction).GetReason(result.Direction);
		}

		private static void ApplyTradeRules(BarData bar, EngineSettings settings, SignalResult result)
		{
			result.RecommendedTradeAction = TradeAction.StandAside;
			result.EntryStyle = "None";
			result.StopLevel = string.Empty;

			if (bar == null || result == null || result.Direction == IntentDirection.Neutral)
				return;

			double threshold = settings.SignalThreshold;
			string entryStyle = "Observe";

			if (result.SignalClassification == IntentSignalClassification.Continuation)
			{
				threshold = settings.ContinuationTradeThreshold;
				entryStyle = "Follow";
			}
			else if (result.SignalClassification == IntentSignalClassification.Reversal)
			{
				threshold = settings.ReversalTradeThreshold;
				entryStyle = "ConfirmThenEnter";
			}
			else if (result.SignalClassification == IntentSignalClassification.Pullback)
			{
				threshold = settings.PullbackTradeThreshold;
				entryStyle = "ReducedSize";
			}

			if (result.IntentScore < threshold)
			{
				result.EntryStyle = "Observe";
				return;
			}

			result.RecommendedTradeAction = result.Direction == IntentDirection.Bullish ? TradeAction.Buy : TradeAction.Sell;
			result.EntryStyle = entryStyle;
			result.StopLevel = BuildStopLevel(bar, result.Direction);
		}

		private static string BuildStopLevel(BarData bar, IntentDirection direction)
		{
			if (bar == null)
				return string.Empty;

			if (direction == IntentDirection.Bullish)
				return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0.#####}", bar.PriorSwingLow);
			if (direction == IntentDirection.Bearish)
				return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0.#####}", bar.PriorSwingHigh);

			return string.Empty;
		}

		private static IntentDirection DetermineTrendDirection(BarData bar, EngineSettings settings)
		{
			if (bar == null)
				return IntentDirection.Neutral;

			if (bar.PriorIntentScore >= settings.SignalThreshold && bar.PriorSignalDirection != IntentDirection.Neutral)
				return bar.PriorSignalDirection;

			double priorRange = bar.PriorSwingHigh - bar.PriorSwingLow;
			if (priorRange <= 0)
				return IntentDirection.Neutral;

			if (bar.Close >= bar.PriorSwingHigh || bar.BreakAboveTicks >= settings.BreakoutExcursionTicks)
				return IntentDirection.Bullish;
			if (bar.Close <= bar.PriorSwingLow || bar.BreakBelowTicks >= settings.BreakoutExcursionTicks)
				return IntentDirection.Bearish;

			double relativeClose = SignalMath.SafeRatio(bar.Close - bar.PriorSwingLow, priorRange);
			if (relativeClose >= settings.BullishTrendStructureThreshold)
				return IntentDirection.Bullish;
			if (relativeClose <= settings.BearishTrendStructureThreshold)
				return IntentDirection.Bearish;

			return IntentDirection.Neutral;
		}

		private static IntentSignalClassification ClassifySignal(BarData bar, EngineSettings settings, SignalResult result)
		{
			if (result == null || result.Direction == IntentDirection.Neutral)
				return IntentSignalClassification.Neutral;

			if (result.TrendDirection == IntentDirection.Neutral)
				return IsReversalCandidate(bar, settings, result, result.Direction) ? IntentSignalClassification.Reversal : IntentSignalClassification.Continuation;

			if (result.TrendDirection == result.Direction)
				return IntentSignalClassification.Continuation;

			return IsReversalCandidate(bar, settings, result, result.Direction) ? IntentSignalClassification.Reversal : IntentSignalClassification.Pullback;
		}

		private static bool IsReversalCandidate(BarData bar, EngineSettings settings, SignalResult result, IntentDirection direction)
		{
			if (bar == null || result == null)
				return false;

			SignalScore dominantSignal = result.GetDominantSignal(direction);
			IntentSignalType dominantSignalType = dominantSignal == null ? IntentSignalType.OrderFlowImbalance : dominantSignal.SignalType;
			double dominantScore = dominantSignal == null ? 0 : dominantSignal.GetScore(direction);
			double trapScore = Math.Max(result.FailedBreakout.GetScore(direction), result.LiquiditySweep.GetScore(direction));
			bool trapStructure = direction == IntentDirection.Bullish
				? bar.BreakBelowTicks >= settings.BreakoutExcursionTicks && bar.ReclaimAboveLowTicks >= settings.ReclaimTicks
				: bar.BreakAboveTicks >= settings.BreakoutExcursionTicks && bar.ReclaimBelowHighTicks >= settings.ReclaimTicks;
			bool structuralReclaim = direction == IntentDirection.Bullish
				? bar.ReclaimAboveLowTicks >= settings.ReclaimTicks || bar.CloseLocation >= settings.ReversalCloseLocationThreshold
				: bar.ReclaimBelowHighTicks >= settings.ReclaimTicks || bar.CloseLocation <= (1.0 - settings.ReversalCloseLocationThreshold);

			bool trapSignal = trapScore >= settings.SignalThreshold
				&& (dominantSignalType == IntentSignalType.FailedBreakout
					|| dominantSignalType == IntentSignalType.LiquiditySweep
					|| trapScore >= (dominantScore - 5));
			if (!trapSignal && trapStructure && trapScore >= (settings.SignalThreshold - 10))
				trapSignal = true;
			bool absorbedExhaustion = dominantSignal != null
				&& dominantSignalType == IntentSignalType.Absorption
				&& result.Absorption.GetScore(direction) >= (settings.SignalThreshold + 5);
			bool confluence = CountStrongSignals(result, direction, settings.SignalThreshold) >= 2;

			return structuralReclaim && (trapSignal || (absorbedExhaustion && confluence));
		}

		private static void ApplyContradictorySignalSuppression(SignalResult result, EngineSettings settings)
		{
			ApplyContradictionForDirection(result.Imbalance, result.Absorption, IntentDirection.Bullish, settings);
			ApplyContradictionForDirection(result.Imbalance, result.Absorption, IntentDirection.Bearish, settings);
		}

		private static void ApplyContradictionForDirection(SignalScore first, SignalScore second, IntentDirection direction, EngineSettings settings)
		{
			if (first.GetScore(direction) < settings.SignalThreshold || second.GetScore(direction) < settings.SignalThreshold)
				return;

			if (first.GetScore(direction) <= second.GetScore(direction))
				first.ScaleScore(direction, settings.ContradictionSuppressionFactor, "Contradiction suppression", "Imbalance suppressed because absorption already explained the bar.");
			else
				second.ScaleScore(direction, settings.ContradictionSuppressionFactor, "Contradiction suppression", "Absorption suppressed because imbalance already explained the bar.");
		}

		private static void ApplyPriorSignalContext(BarData bar, EngineSettings settings, SignalResult result, ref SignalFactor[] bullishScoreFactors, ref SignalFactor[] bearishScoreFactors)
		{
			if (bar == null || bar.PriorIntentScore < settings.SignalThreshold || bar.PriorSignalDirection == IntentDirection.Neutral)
				return;

			if (bar.PriorSignalDirection == IntentDirection.Bullish)
			{
				result.BullScore = SignalMath.Clamp100(result.BullScore + settings.PriorSignalConfirmationBonus);
				result.BearScore = SignalMath.Clamp100(result.BearScore * settings.PriorSignalOppositionMultiplier);
				AppendFactor(ref bullishScoreFactors, CreateFactor("Prior signal alignment", bar.PriorIntentScore, 1.0, 1.0, "Previous bar aligned bullish.", settings.PriorSignalConfirmationBonus));
				AppendFactor(ref bearishScoreFactors, CreateFactor("Prior signal opposition", bar.PriorIntentScore, settings.PriorSignalOppositionMultiplier, 1.0, "Previous bar opposed bearish continuation.", result.BearScore));
			}
			else if (bar.PriorSignalDirection == IntentDirection.Bearish)
			{
				result.BearScore = SignalMath.Clamp100(result.BearScore + settings.PriorSignalConfirmationBonus);
				result.BullScore = SignalMath.Clamp100(result.BullScore * settings.PriorSignalOppositionMultiplier);
				AppendFactor(ref bearishScoreFactors, CreateFactor("Prior signal alignment", bar.PriorIntentScore, 1.0, 1.0, "Previous bar aligned bearish.", settings.PriorSignalConfirmationBonus));
				AppendFactor(ref bullishScoreFactors, CreateFactor("Prior signal opposition", bar.PriorIntentScore, settings.PriorSignalOppositionMultiplier, 1.0, "Previous bar opposed bullish continuation.", result.BullScore));
			}
		}

		private static int CountStrongSignals(SignalResult result, IntentDirection direction, int signalThreshold)
		{
			int count = 0;
			bool bothImbalanceAndAbsorption = result.Imbalance.GetScore(direction) >= signalThreshold
				&& result.Absorption.GetScore(direction) >= signalThreshold;
			bool countedImbalanceAbsorption = false;

			foreach (SignalScore signal in result.Signals)
			{
				if (signal.GetScore(direction) < signalThreshold)
					continue;

				if (bothImbalanceAndAbsorption && (signal == result.Imbalance || signal == result.Absorption))
				{
					if (!countedImbalanceAbsorption)
					{
						countedImbalanceAbsorption = true;
						count++;
					}
					continue;
				}

				count++;
			}
			return count;
		}

		private static SignalFactor CreateDirectionalFactor(string name, double rawValue, bool matched, double weight, string detail)
		{
			return CreateFactor(name, rawValue, matched ? 1.0 : 0.0, weight, detail);
		}

		private static double ContradictionPenalty(double contradictoryDeltaPerVolume, EngineSettings settings)
		{
			double normalized = NormalizeAbove(contradictoryDeltaPerVolume, settings.DeltaPerVolumeBaseline, settings.DeltaPerVolumeNormalizationSpan);
			return 1.0 - ((1.0 - settings.ContradictionPenaltyFloorMultiplier) * normalized);
		}

		private static double NormalizeAbove(double value, double baseline, double span)
		{
			if (span <= 0)
				return value > baseline ? 1.0 : 0.0;

			return SignalMath.Clamp01((value - baseline) / span);
		}

		private static double NormalizeBelow(double value, double ceiling, double span)
		{
			if (span <= 0)
				return value < ceiling ? 1.0 : 0.0;

			return SignalMath.Clamp01((ceiling - value) / span);
		}

		private static SignalFactor CreateFactor(string name, double rawValue, double normalizedValue, double weight, string detail)
		{
			return CreateFactor(name, rawValue, normalizedValue, weight, detail, normalizedValue * weight);
		}

		private static SignalFactor CreateFactor(string name, double rawValue, double normalizedValue, double weight, string detail, double contribution)
		{
			return new SignalFactor
			{
				Name = name,
				RawValue = rawValue,
				NormalizedValue = normalizedValue,
				Weight = weight,
				Contribution = contribution,
				Detail = detail
			};
		}

		private static double SumContributions(ICollection<SignalFactor> factors)
		{
			double score = 0;
			foreach (SignalFactor factor in factors)
				score += factor.Contribution;
			return score;
		}

		private static void AppendAdjustedFactor(ref SignalFactor[] factors, string name, double rawValue, double multiplier, double adjustedScore, string detail)
		{
			AppendFactor(ref factors, CreateFactor(name, rawValue, multiplier, 1.0, detail, adjustedScore));
		}

		private static void AppendFactor(ref SignalFactor[] factors, SignalFactor factor)
		{
			List<SignalFactor> list = new List<SignalFactor>(factors);
			list.Add(factor);
			factors = list.ToArray();
		}
	}
}
