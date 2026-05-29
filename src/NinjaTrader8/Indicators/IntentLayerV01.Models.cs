#region Using declarations
using System.Windows.Media;
using NinjaTrader.Gui.Tools;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
	internal sealed class IntentVisualTheme
	{
		public Brush BullishBrush { get; set; }
		public Brush BearishBrush { get; set; }
		public Brush NeutralBrush { get; set; }
		public Brush PanelBackgroundBrush { get; set; }
		public Brush PanelBorderBrush { get; set; }
		public SimpleFont DebugFont { get; set; }
	}
}
