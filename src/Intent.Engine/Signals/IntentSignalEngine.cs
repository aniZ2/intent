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
					CreateFactor("Delta per volume", bar.OrderFlow.DeltaPerVolume, NormalizeAbove(bar.OrderFlow.DeltaPerVolume, settings.DeltaPerVolumeBaseline, settings.DeltaPerVolumeNormalizationSpan), 20, "Positive delta supported by volume."),
					CreateFactor("Close location", bar.CloseLocation, NormalizeAbove(bar.CloseLocation, 0.50, settings.CloseLocationNormalizationSpan), 20, "Close holding near the bar high.")
				};

				SignalFactor[] bearishFactors = new[]
				{
					CreateFactor("Bid imbalance levels", bar.OrderFlow.BidImbalanceLevels, NormalizeAbove(bar.OrderFlow.BidImbalanceLevels, 1.0, settings.ImbalanceLevelNormalizationSpan), 35, "Stacked bid-side imbalance."),
					CreateFactor("Bid imbalance ratio", bar.OrderFlow.BidImbalanceRatio, NormalizeAbove(bar.OrderFlow.BidImbalanceRatio, settings.ImbalanceRatioThreshold, settings.ImbalanceRatioNormalizationSpan), 25, "Strong bid-over-ask ratio."),
					CreateFactor("Delta per volume", bar.OrderFlow.DeltaPerVolume, NormalizeAbove(bar.OrderFlow.DeltaPerVolume, settings.DeltaPerVolumeBaseline, settings.DeltaPerVolumeNormalizationSpan), 20, "Negative delta supported by volume."),
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
					CreateFactor("Delta per volume", bar.OrderFlow.DeltaPerVolume, NormalizeAbove(Math.Abs(bar.OrderFlow.DeltaPerVolume), settings.AbsorptionDeltaThresholdRatio, settings.DeltaPerVolumeNormalizationSpan), 35, "Delta was large relative to volume."),
					CreateFactor("Price efficiency", bar.PriceEfficiency, NormalizeBelow(bar.PriceEfficiency, settings.AbsorptionPriceEfficiencyThreshold, settings.AbsorptionPriceEfficiencyThreshold), 20, "Price barely moved despite pressure."),
					CreateFactor("Close location", bar.CloseLocation, NormalizeAbove(bar.CloseLocation, 0.55, settings.FallbackCloseLocationNormalizationSpan), 15, "Close held away from the sell pressure.")
				};

				SignalFactor[] bearishFactors = new[]
				{
					CreateFactor("Opposing delta", bar.OrderFlow.BarDelta, bar.OrderFlow.BarDelta > 0 ? NormalizeAbove(Math.Abs(bar.OrderFlow.DeltaPerVolume), settings.AbsorptionDeltaThresholdRatio, settings.DeltaPerVolumeNormalizationSpan) : 0, 30, "Buying pressure was absorbed."),
					CreateFactor("Delta per volume", bar.OrderFlow.DeltaPerVolume, NormalizeAbove(Math.Abs(bar.OrderFlow.DeltaPerVolume), settings.AbsorptionDeltaThresholdRatio, settings.DeltaPerVolumeNormalizationSpan), 35, "Delta was large relative to volume."),
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
				CreateFactor("Close location", bar.CloseLocation, NormalizeBelow(bar.CloseLocation, 0.55, 0.55), 15, "Close rotated back toward the lows."),
				CreateDirectionalFactor("Bar delta confirmation", bar.OrderFlow != null ? bar.OrderFlow.BarDelta : 0, bar.OrderFlow != null && bar.OrderFlow.IsAvailable && bar.OrderFlow.BarDelta < 0, 10, "Order flow confirmed the bearish trap."),
				CreateFactor("Bid imbalance levels", bar.OrderFlow != null ? bar.OrderFlow.BidImbalanceLevels : 0, bar.OrderFlow != null && bar.OrderFlow.IsAvailable ? NormalizeAbove(bar.OrderFlow.BidImbalanceLevels, 1.0, settings.ImbalanceLevelNormalizationSpan) : 0, 10, "Bid imbalance expanded on the reversal."),
				CreateFactor("Breakout zone confirmation", bearishZoneConfirmation, bearishZoneConfirmation, 15, "Negative delta accumulated above the failed breakout level.")
			};

			SignalFactor[] bullishFactors = new[]
			{
				CreateFactor("Break below ticks", bar.BreakBelowTicks, NormalizeAbove(bar.BreakBelowTicks, settings.BreakoutExcursionTicks, settings.BreakoutNormalizationSpan), 35, "Price extended beyond the prior low."),
				CreateFactor("Reclaim above low", bar.ReclaimAboveLowTicks, NormalizeAbove(bar.ReclaimAboveLowTicks, settings.ReclaimTicks, settings.BreakoutNormalizationSpan), 25, "Breakout failed to hold below the prior low."),
				CreateFactor("Close location", bar.CloseLocation, NormalizeAbove(bar.CloseLocation, 0.45, 0.55), 15, "Close rotated back toward the highs."),
				CreateDirectionalFactor("Bar delta confirmation", bar.OrderFlow != null ? bar.OrderFlow.BarDelta : 0, bar.OrderFlow != null && bar.OrderFlow.IsAvailable && bar.OrderFlow.BarDelta > 0, 10, "Order flow confirmed the bullish trap."),
				CreateFactor("Ask imbalance levels", bar.OrderFlow != null ? bar.OrderFlow.AskImbalanceLevels : 0, bar.OrderFlow != null && bar.OrderFlow.IsAvailable ? NormalizeAbove(bar.OrderFlow.AskImbalanceLevels, 1.0, settings.ImbalanceLevelNormalizationSpan) : 0, 10, "Ask imbalance expanded on the reversal."),
				CreateFactor("Breakout zone confirmation", bullishZoneConfirmation, bullishZoneConfirmation, 15, "Positive delta accumulated below the failed breakout level.")
			};

			score.SetScores(SumContributions(bullishFactors), SumContributions(bearishFactors), "Break below prior low failed and reclaimed", "Break above prior high failed and reclaimed", bullishFactors, bearishFactors);
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

			score.SetScores(SumContributions(bullishFactors), SumContributions(bearishFactors), "Sell-side sweep and fast reclaim", "Buy-side sweep and fast reclaim", bullishFactors, bearishFactors);
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

		private static void FinalizeScores(BarData bar, EngineSettings settings, SignalResult result)
		{
			ApplyContradictorySignalSuppression(result, settings);

			SignalFactor[] bullishScoreFactors = new[]
			{
				CreateFactor("Imbalance weighted", result.Imbalance.Bullish, result.Imbalance.Bullish / 100.0, settings.ImbalanceWeight * 100.0, "Weighted imbalance contribution."),
				CreateFactor("Absorption weighted", result.Absorption.Bullish, result.Absorption.Bullish / 100.0, settings.AbsorptionWeight * 100.0, "Weighted absorption contribution."),
				CreateFactor("Failed breakout weighted", result.FailedBreakout.Bullish, result.FailedBreakout.Bullish / 100.0, settings.FailedBreakoutWeight * 100.0, "Weighted failed-breakout contribution."),
				CreateFactor("Liquidity sweep weighted", result.LiquiditySweep.Bullish, result.LiquiditySweep.Bullish / 100.0, settings.LiquiditySweepWeight * 100.0, "Weighted sweep contribution.")
			};

			SignalFactor[] bearishScoreFactors = new[]
			{
				CreateFactor("Imbalance weighted", result.Imbalance.Bearish, result.Imbalance.Bearish / 100.0, settings.ImbalanceWeight * 100.0, "Weighted imbalance contribution."),
				CreateFactor("Absorption weighted", result.Absorption.Bearish, result.Absorption.Bearish / 100.0, settings.AbsorptionWeight * 100.0, "Weighted absorption contribution."),
				CreateFactor("Failed breakout weighted", result.FailedBreakout.Bearish, result.FailedBreakout.Bearish / 100.0, settings.FailedBreakoutWeight * 100.0, "Weighted failed-breakout contribution."),
				CreateFactor("Liquidity sweep weighted", result.LiquiditySweep.Bearish, result.LiquiditySweep.Bearish / 100.0, settings.LiquiditySweepWeight * 100.0, "Weighted sweep contribution.")
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
				result.Direction = IntentDirection.Neutral;
				result.DominantReason = "No dominant intent";
				return;
			}

			result.Direction = result.BullScore > result.BearScore ? IntentDirection.Bullish : IntentDirection.Bearish;
			result.DominantReason = result.GetDominantSignal(result.Direction).GetReason(result.Direction);
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
			foreach (SignalScore signal in result.Signals)
			{
				if ((signal == result.Imbalance || signal == result.Absorption) && result.Imbalance.GetScore(direction) >= signalThreshold && result.Absorption.GetScore(direction) >= signalThreshold)
					continue;
				if (signal.GetScore(direction) >= signalThreshold)
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
