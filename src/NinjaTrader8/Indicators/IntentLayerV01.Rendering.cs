#region Using declarations
using System.Text;
using System.Windows;
using System.Windows.Media;
using Intent.Engine.Models;
using Intent.Engine.Signals;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
	internal sealed class IntentChartRenderer
	{
		private readonly IntentLayerV01 owner;
		private readonly IntentVisualTheme theme;

		public IntentChartRenderer(IntentLayerV01 owner, IntentVisualTheme theme)
		{
			this.owner = owner;
			this.theme = theme;
		}

		public void RenderWarmup(bool showDebugPanel)
		{
			owner.IntentScore[0] = 0;
			owner.BullScore[0] = 0;
			owner.BearScore[0] = 0;
			owner.BarBrush = null;

			if (!showDebugPanel)
				return;

			Draw.TextFixed(owner, IntentTags.DebugPanel, "IntentLayerV01\nwarming up...", TextPosition.TopLeft, Brushes.Gainsboro, theme.DebugFont, theme.PanelBackgroundBrush, theme.PanelBorderBrush, 60);
		}

		public void Render(BarData bar, SignalResult analysis, int signalThreshold, bool highlightBars, bool showDebugPanel)
		{
			owner.IntentScore[0] = analysis.IntentScore;
			owner.BullScore[0] = analysis.BullScore;
			owner.BearScore[0] = analysis.BearScore;

			ApplyBarHighlight(analysis, highlightBars, signalThreshold);
			DrawSignalMarkers(analysis, signalThreshold);
			DrawCompositeMarker(analysis, signalThreshold);
			DrawDebugPanel(bar, analysis, showDebugPanel);
		}

		private void ApplyBarHighlight(SignalResult analysis, bool highlightBars, int signalThreshold)
		{
			if (!highlightBars)
			{
				owner.BarBrush = null;
				return;
			}

			if (analysis.Direction == IntentDirection.Bullish && analysis.IntentScore >= signalThreshold)
				owner.BarBrush = theme.BullishBrush;
			else if (analysis.Direction == IntentDirection.Bearish && analysis.IntentScore >= signalThreshold)
				owner.BarBrush = theme.BearishBrush;
			else
				owner.BarBrush = null;
		}

		private void DrawSignalMarkers(SignalResult analysis, int signalThreshold)
		{
			int offset = 1;
			foreach (SignalScore signal in analysis.Signals)
			{
				if (signal.IsTriggered(IntentDirection.Bullish, signalThreshold))
					Draw.ArrowUp(owner, IntentTags.ForSignal(signal.SignalType, IntentDirection.Bullish, owner.CurrentBar), false, 0, owner.Low[0] - owner.TickSize * offset, theme.BullishBrush);

				if (signal.IsTriggered(IntentDirection.Bearish, signalThreshold))
					Draw.ArrowDown(owner, IntentTags.ForSignal(signal.SignalType, IntentDirection.Bearish, owner.CurrentBar), false, 0, owner.High[0] + owner.TickSize * offset, theme.BearishBrush);

				offset++;
			}
		}

		private void DrawCompositeMarker(SignalResult analysis, int signalThreshold)
		{
			if (analysis.IntentScore < signalThreshold || analysis.Direction == IntentDirection.Neutral)
				return;

			Brush dominantBrush = analysis.Direction == IntentDirection.Bullish ? theme.BullishBrush : theme.BearishBrush;
			double y = analysis.Direction == IntentDirection.Bullish ? owner.Low[0] - owner.TickSize * 5 : owner.High[0] + owner.TickSize * 5;
			string text = analysis.Direction == IntentDirection.Bullish
				? string.Format("BULL {0:0}", analysis.IntentScore)
				: string.Format("BEAR {0:0}", analysis.IntentScore);

			Draw.Text(owner, IntentTags.Composite(owner.CurrentBar), false, text, 0, y, 0, dominantBrush, theme.DebugFont, TextAlignment.Center, Brushes.Transparent, Brushes.Transparent, 0);
		}

		private void DrawDebugPanel(BarData bar, SignalResult analysis, bool showDebugPanel)
		{
			if (!showDebugPanel)
			{
				owner.RemoveDrawObject(IntentTags.DebugPanel);
				return;
			}

			StringBuilder builder = new StringBuilder(320);
			builder.AppendLine("IntentLayerV01");
			builder.AppendLine(string.Format("Score {0:0}  Bull {1:0}  Bear {2:0}", analysis.IntentScore, analysis.BullScore, analysis.BearScore));
			builder.AppendLine(string.Format("Bias  {0}", analysis.Direction));
			builder.AppendLine(string.Format("Lead  {0}", analysis.DominantReason));
			builder.AppendLine(string.Format("Vol   {0:0}  Avg {1:0}  Spike {2:0.00}x", bar.Volume, bar.AverageVolume, bar.VolumeSpike));
			builder.AppendLine(bar.OrderFlow != null && bar.OrderFlow.IsAvailable
				? string.Format("OF    D {0:+0;-0;0}  Ask {1:0}  Bid {2:0}", bar.OrderFlow.BarDelta, bar.OrderFlow.TotalBuyingVolume, bar.OrderFlow.TotalSellingVolume)
				: "OF    N/A (requires volumetric bars)");
			builder.AppendLine(bar.OrderFlow != null && bar.OrderFlow.IsAvailable
				? string.Format("Imb   Ask {0} ({1:0.00})  Bid {2} ({3:0.00})", bar.OrderFlow.AskImbalanceLevels, bar.OrderFlow.AskImbalanceRatio, bar.OrderFlow.BidImbalanceLevels, bar.OrderFlow.BidImbalanceRatio)
				: "Imb   N/A");
			builder.AppendLine(string.Format("Rng   {0:0.00}  Avg {1:0.00}  Exp {2:0.00}x", bar.Range, bar.AverageRange, bar.RangeExpansion));
			builder.AppendLine(string.Format("Imb   B {0:0} | S {1:0}", analysis.Imbalance.Bullish, analysis.Imbalance.Bearish));
			builder.AppendLine(string.Format("Abs   B {0:0} | S {1:0}", analysis.Absorption.Bullish, analysis.Absorption.Bearish));
			builder.AppendLine(string.Format("Fail  B {0:0} | S {1:0}", analysis.FailedBreakout.Bullish, analysis.FailedBreakout.Bearish));
			builder.Append(string.Format("Sweep B {0:0} | S {1:0}", analysis.LiquiditySweep.Bullish, analysis.LiquiditySweep.Bearish));

			Brush textBrush = analysis.Direction == IntentDirection.Bearish
				? theme.BearishBrush
				: analysis.Direction == IntentDirection.Bullish
					? theme.BullishBrush
					: theme.NeutralBrush;

			Draw.TextFixed(owner, IntentTags.DebugPanel, builder.ToString(), TextPosition.TopLeft, textBrush, theme.DebugFont, theme.PanelBackgroundBrush, theme.PanelBorderBrush, 70);
		}
	}

	internal static class IntentTags
	{
		public const string DebugPanel = "IntentLayerV01.Debug";

		public static string Composite(int currentBar)
		{
			return "IntentLayerV01.Composite." + currentBar;
		}

		public static string ForSignal(IntentSignalType signalType, IntentDirection direction, int currentBar)
		{
			return string.Format("IntentLayerV01.{0}.{1}.{2}", signalType, direction, currentBar);
		}
	}
}
