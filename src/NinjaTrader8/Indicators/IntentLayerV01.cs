//
// IntentLayerV01
// Price/volume intent detector for NinjaTrader 8
//
#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Xml.Serialization;
using System.Windows.Media;
using Intent.Engine.Models;
using Intent.Engine.Signals;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
	public class IntentLayerV01 : Indicator
	{
		private IntentVisualTheme visualTheme;
		private IIntentPlatformAdapter adapter;
		private IntentSignalEngine engine;
		private IntentChartRenderer renderer;
		private ITickStreamPublisher tickPublisher;
		private double lastBidPrice;
		private double lastAskPrice;
		private IntentDirection previousSignalDirection;
		private double previousIntentScore;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Name = "IntentLayerV01";
				Description = "Detects order-flow style imbalance, absorption, failed breakouts, and liquidity sweeps.";
				Calculate = Calculate.OnBarClose;
				IsOverlay = false;
				DrawOnPricePanel = true;
				DisplayInDataBox = true;
				IsSuspendedWhileInactive = true;

				VolumeLookback = 20;
				RangeLookback = 14;
				StructureLookback = 20;
				SignalThreshold = 60;
				ImbalanceVolumeSpikeThreshold = 1.15;
				AbsorptionVolumeSpikeThreshold = 1.20;
				AbsorptionWickThreshold = 0.35;
				SweepVolumeSpikeThreshold = 1.35;
				SweepWickThreshold = 0.40;
				BreakoutExcursionTicks = 2;
				ReclaimTicks = 1;
				ImbalanceRatioThreshold = 2.5;
				AbsorptionDeltaThresholdRatio = 0.22;
				AbsorptionPriceEfficiencyThreshold = 0.35;
				MinImbalanceVolumePerLevel = 15;
				ImbalanceWeight = 0.35;
				AbsorptionWeight = 0.20;
				FailedBreakoutWeight = 0.20;
				LiquiditySweepWeight = 0.25;
				ConfluenceBonus = 8;
				ExpansiveVolumeBonus = 4;
				NeutralityBuffer = 5;
				ImbalanceLevelNormalizationSpan = 4;
				ImbalanceRatioNormalizationSpan = 3;
				DeltaPerVolumeBaseline = 0.10;
				DeltaPerVolumeNormalizationSpan = 0.40;
				CloseLocationNormalizationSpan = 0.50;
				FallbackCloseLocationNormalizationSpan = 0.45;
				BodyRatioBaseline = 0.35;
				BodyRatioNormalizationSpan = 0.55;
				VolumeSpikeNormalizationSpan = 1.5;
				AbsorptionWickNormalizationSpan = 0.65;
				RangeExpansionPenaltyThreshold = 1.25;
				RangeExpansionNormalizationBaseline = 1.0;
				RangeExpansionNormalizationSpan = 1.5;
				BreakoutNormalizationSpan = 8;
				SweepWickNormalizationSpan = 0.6;
				SweepVolumeNormalizationSpan = 1.75;
				BreakoutZoneDeltaBaseline = 0.05;
				BreakoutZoneDeltaNormalizationSpan = 0.35;
				ExpansiveVolumeRangeExpansionThreshold = 1.2;
				ShowDebugPanel = true;
				HighlightBars = true;
				EnableTickStreaming = false;
				StreamHost = "127.0.0.1";
				StreamPort = 4100;
				BarsRequiredToPlot = 20;

				AddPlot(Brushes.DodgerBlue, "IntentScore");
				AddPlot(Brushes.ForestGreen, "BullScore");
				AddPlot(Brushes.IndianRed, "BearScore");
			}
			else if (State == State.Configure)
			{
				visualTheme = BuildVisualTheme();
			}
			else if (State == State.DataLoaded)
			{
				adapter = new NinjaTraderIntentAdapter(this);
				engine = new IntentSignalEngine();
				renderer = new IntentChartRenderer(this, visualTheme);
				if (EnableTickStreaming)
					tickPublisher = new TcpTickStreamPublisher(StreamHost, StreamPort, Print);
			}
			else if (State == State.Terminated)
			{
				RemoveDrawObject(IntentTags.DebugPanel);
				if (tickPublisher != null)
					tickPublisher.Dispose();
			}
		}

		protected override void OnBarUpdate()
		{
			if (CurrentBar < RequiredBars)
			{
				renderer.RenderWarmup(ShowDebugPanel);
				return;
			}

			EngineSettings settings = adapter.BuildSettings();
			BarData bar = adapter.BuildBarData(settings);
			SignalResult analysis = engine.Analyze(bar, settings);
			previousSignalDirection = analysis.Direction;
			previousIntentScore = analysis.IntentScore;
			renderer.Render(bar, analysis, settings.SignalThreshold, HighlightBars, ShowDebugPanel);
		}

		protected override void OnMarketData(MarketDataEventArgs marketDataUpdate)
		{
			if (marketDataUpdate == null)
				return;

			if (marketDataUpdate.MarketDataType == MarketDataType.Bid)
			{
				lastBidPrice = marketDataUpdate.Price;
				return;
			}

			if (marketDataUpdate.MarketDataType == MarketDataType.Ask)
			{
				lastAskPrice = marketDataUpdate.Price;
				return;
			}

			if (marketDataUpdate.MarketDataType != MarketDataType.Last)
				return;

			if (EnableTickStreaming)
				Print(string.Format("[Intent.Stream] OnMarketData Last price={0} state={1} publisher={2}", marketDataUpdate.Price, State, tickPublisher != null ? "ready" : "null"));

			if (!EnableTickStreaming || tickPublisher == null || State != State.Realtime)
				return;

			TickData tick = adapter.BuildTickData(marketDataUpdate);
			if (tick != null)
			{
				Print("STREAMING TICK");
				tickPublisher.Publish(tick);
			}
		}

		private int RequiredBars
		{
			get { return Math.Max(StructureLookback, Math.Max(VolumeLookback, RangeLookback)); }
		}

		private IntentVisualTheme BuildVisualTheme()
		{
			return new IntentVisualTheme
			{
				BullishBrush = FreezeBrush(new SolidColorBrush(Color.FromArgb(210, 32, 122, 74))),
				BearishBrush = FreezeBrush(new SolidColorBrush(Color.FromArgb(210, 176, 55, 55))),
				NeutralBrush = FreezeBrush(new SolidColorBrush(Color.FromArgb(210, 90, 102, 114))),
				PanelBackgroundBrush = FreezeBrush(new SolidColorBrush(Color.FromArgb(180, 18, 24, 31))),
				PanelBorderBrush = FreezeBrush(new SolidColorBrush(Color.FromArgb(255, 88, 104, 121))),
				DebugFont = new SimpleFont("Consolas", 12)
			};
		}

		private static Brush FreezeBrush(Brush brush)
		{
			if (brush.CanFreeze)
				brush.Freeze();

			return brush;
		}

		#region Properties
		[Range(5, 200)]
		[NinjaScriptProperty]
		[Display(Name = "VolumeLookback", GroupName = "Parameters", Order = 0)]
		public int VolumeLookback { get; set; }

		[Range(5, 200)]
		[NinjaScriptProperty]
		[Display(Name = "RangeLookback", GroupName = "Parameters", Order = 1)]
		public int RangeLookback { get; set; }

		[Range(5, 200)]
		[NinjaScriptProperty]
		[Display(Name = "StructureLookback", GroupName = "Parameters", Order = 2)]
		public int StructureLookback { get; set; }

		[Range(1, 100)]
		[NinjaScriptProperty]
		[Display(Name = "SignalThreshold", GroupName = "Parameters", Order = 3)]
		public int SignalThreshold { get; set; }

		[Range(0.5, 5.0)]
		[NinjaScriptProperty]
		[Display(Name = "ImbalanceVolumeSpikeThreshold", GroupName = "Thresholds", Order = 4)]
		public double ImbalanceVolumeSpikeThreshold { get; set; }

		[Range(0.5, 5.0)]
		[NinjaScriptProperty]
		[Display(Name = "AbsorptionVolumeSpikeThreshold", GroupName = "Thresholds", Order = 5)]
		public double AbsorptionVolumeSpikeThreshold { get; set; }

		[Range(0.05, 0.95)]
		[NinjaScriptProperty]
		[Display(Name = "AbsorptionWickThreshold", GroupName = "Thresholds", Order = 6)]
		public double AbsorptionWickThreshold { get; set; }

		[Range(0.5, 5.0)]
		[NinjaScriptProperty]
		[Display(Name = "SweepVolumeSpikeThreshold", GroupName = "Thresholds", Order = 7)]
		public double SweepVolumeSpikeThreshold { get; set; }

		[Range(0.05, 0.95)]
		[NinjaScriptProperty]
		[Display(Name = "SweepWickThreshold", GroupName = "Thresholds", Order = 8)]
		public double SweepWickThreshold { get; set; }

		[Range(1, 20)]
		[NinjaScriptProperty]
		[Display(Name = "BreakoutExcursionTicks", GroupName = "Thresholds", Order = 9)]
		public int BreakoutExcursionTicks { get; set; }

		[Range(1, 20)]
		[NinjaScriptProperty]
		[Display(Name = "ReclaimTicks", GroupName = "Thresholds", Order = 10)]
		public int ReclaimTicks { get; set; }

		[Range(1.1, 10.0)]
		[NinjaScriptProperty]
		[Display(Name = "ImbalanceRatioThreshold", GroupName = "OrderFlow", Order = 11)]
		public double ImbalanceRatioThreshold { get; set; }

		[Range(0.01, 1.00)]
		[NinjaScriptProperty]
		[Display(Name = "AbsorptionDeltaThresholdRatio", GroupName = "OrderFlow", Order = 12)]
		public double AbsorptionDeltaThresholdRatio { get; set; }

		[Range(0.05, 1.00)]
		[NinjaScriptProperty]
		[Display(Name = "AbsorptionPriceEfficiencyThreshold", GroupName = "OrderFlow", Order = 13)]
		public double AbsorptionPriceEfficiencyThreshold { get; set; }

		[Range(1, int.MaxValue)]
		[NinjaScriptProperty]
		[Display(Name = "MinImbalanceVolumePerLevel", GroupName = "OrderFlow", Order = 14)]
		public int MinImbalanceVolumePerLevel { get; set; }

		[Range(0.0, 1.0)]
		[NinjaScriptProperty]
		[Display(Name = "ImbalanceWeight", GroupName = "Scoring", Order = 15)]
		public double ImbalanceWeight { get; set; }

		[Range(0.0, 1.0)]
		[NinjaScriptProperty]
		[Display(Name = "AbsorptionWeight", GroupName = "Scoring", Order = 16)]
		public double AbsorptionWeight { get; set; }

		[Range(0.0, 1.0)]
		[NinjaScriptProperty]
		[Display(Name = "FailedBreakoutWeight", GroupName = "Scoring", Order = 17)]
		public double FailedBreakoutWeight { get; set; }

		[Range(0.0, 1.0)]
		[NinjaScriptProperty]
		[Display(Name = "LiquiditySweepWeight", GroupName = "Scoring", Order = 18)]
		public double LiquiditySweepWeight { get; set; }

		[Range(0.0, 100.0)]
		[NinjaScriptProperty]
		[Display(Name = "ConfluenceBonus", GroupName = "Scoring", Order = 19)]
		public double ConfluenceBonus { get; set; }

		[Range(0.0, 100.0)]
		[NinjaScriptProperty]
		[Display(Name = "ExpansiveVolumeBonus", GroupName = "Scoring", Order = 20)]
		public double ExpansiveVolumeBonus { get; set; }

		[Range(0.0, 100.0)]
		[NinjaScriptProperty]
		[Display(Name = "NeutralityBuffer", GroupName = "Scoring", Order = 21)]
		public double NeutralityBuffer { get; set; }

		[Range(0.01, 100.0)]
		[NinjaScriptProperty]
		[Display(Name = "ImbalanceLevelNormSpan", GroupName = "AdvancedNormalization", Order = 22)]
		public double ImbalanceLevelNormalizationSpan { get; set; }

		[Range(0.01, 100.0)]
		[NinjaScriptProperty]
		[Display(Name = "ImbalanceRatioNormSpan", GroupName = "AdvancedNormalization", Order = 23)]
		public double ImbalanceRatioNormalizationSpan { get; set; }

		[Range(0.0, 100.0)]
		[NinjaScriptProperty]
		[Display(Name = "DeltaPerVolumeBaseline", GroupName = "AdvancedNormalization", Order = 24)]
		public double DeltaPerVolumeBaseline { get; set; }

		[Range(0.01, 100.0)]
		[NinjaScriptProperty]
		[Display(Name = "DeltaPerVolumeNormSpan", GroupName = "AdvancedNormalization", Order = 25)]
		public double DeltaPerVolumeNormalizationSpan { get; set; }

		[Range(0.01, 1.0)]
		[NinjaScriptProperty]
		[Display(Name = "CloseLocationNormSpan", GroupName = "AdvancedNormalization", Order = 26)]
		public double CloseLocationNormalizationSpan { get; set; }

		[Range(0.01, 1.0)]
		[NinjaScriptProperty]
		[Display(Name = "FallbackCloseLocNormSpan", GroupName = "AdvancedNormalization", Order = 27)]
		public double FallbackCloseLocationNormalizationSpan { get; set; }

		[Range(0.0, 1.0)]
		[NinjaScriptProperty]
		[Display(Name = "BodyRatioBaseline", GroupName = "AdvancedNormalization", Order = 28)]
		public double BodyRatioBaseline { get; set; }

		[Range(0.01, 1.0)]
		[NinjaScriptProperty]
		[Display(Name = "BodyRatioNormSpan", GroupName = "AdvancedNormalization", Order = 29)]
		public double BodyRatioNormalizationSpan { get; set; }

		[Range(0.01, 100.0)]
		[NinjaScriptProperty]
		[Display(Name = "VolumeSpikeNormSpan", GroupName = "AdvancedNormalization", Order = 30)]
		public double VolumeSpikeNormalizationSpan { get; set; }

		[Range(0.01, 1.0)]
		[NinjaScriptProperty]
		[Display(Name = "AbsorptionWickNormSpan", GroupName = "AdvancedNormalization", Order = 31)]
		public double AbsorptionWickNormalizationSpan { get; set; }

		[Range(0.0, 100.0)]
		[NinjaScriptProperty]
		[Display(Name = "RangeExpansionPenalty", GroupName = "AdvancedNormalization", Order = 32)]
		public double RangeExpansionPenaltyThreshold { get; set; }

		[Range(0.0, 100.0)]
		[NinjaScriptProperty]
		[Display(Name = "RangeExpansionBaseline", GroupName = "AdvancedNormalization", Order = 33)]
		public double RangeExpansionNormalizationBaseline { get; set; }

		[Range(0.01, 100.0)]
		[NinjaScriptProperty]
		[Display(Name = "RangeExpansionNormSpan", GroupName = "AdvancedNormalization", Order = 34)]
		public double RangeExpansionNormalizationSpan { get; set; }

		[Range(0.01, 100.0)]
		[NinjaScriptProperty]
		[Display(Name = "BreakoutNormSpan", GroupName = "AdvancedNormalization", Order = 35)]
		public double BreakoutNormalizationSpan { get; set; }

		[Range(0.01, 1.0)]
		[NinjaScriptProperty]
		[Display(Name = "SweepWickNormSpan", GroupName = "AdvancedNormalization", Order = 36)]
		public double SweepWickNormalizationSpan { get; set; }

		[Range(0.01, 100.0)]
		[NinjaScriptProperty]
		[Display(Name = "SweepVolumeNormSpan", GroupName = "AdvancedNormalization", Order = 37)]
		public double SweepVolumeNormalizationSpan { get; set; }

		[Range(0.0, 100.0)]
		[NinjaScriptProperty]
		[Display(Name = "BreakoutZoneDeltaBase", GroupName = "AdvancedNormalization", Order = 38)]
		public double BreakoutZoneDeltaBaseline { get; set; }

		[Range(0.01, 100.0)]
		[NinjaScriptProperty]
		[Display(Name = "BreakoutZoneDeltaNorm", GroupName = "AdvancedNormalization", Order = 39)]
		public double BreakoutZoneDeltaNormalizationSpan { get; set; }

		[Range(0.0, 100.0)]
		[NinjaScriptProperty]
		[Display(Name = "ExpansiveVolumeRangeExp", GroupName = "AdvancedNormalization", Order = 40)]
		public double ExpansiveVolumeRangeExpansionThreshold { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "ShowDebugPanel", GroupName = "Visual", Order = 41)]
		public bool ShowDebugPanel { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "HighlightBars", GroupName = "Visual", Order = 42)]
		public bool HighlightBars { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "EnableTickStreaming", GroupName = "Streaming", Order = 43)]
		public bool EnableTickStreaming { get; set; }

		[Display(Name = "StreamHost", GroupName = "Streaming", Order = 44)]
		public string StreamHost { get; set; }

		[Range(1, 65535)]
		[NinjaScriptProperty]
		[Display(Name = "StreamPort", GroupName = "Streaming", Order = 45)]
		public int StreamPort { get; set; }

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> IntentScore
		{
			get { return Values[0]; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> BullScore
		{
			get { return Values[1]; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> BearScore
		{
			get { return Values[2]; }
		}

		[Browsable(false)]
		public double LastBidPrice
		{
			get { return lastBidPrice; }
		}

		[Browsable(false)]
		public double LastAskPrice
		{
			get { return lastAskPrice; }
		}

		[Browsable(false)]
		public IntentDirection PreviousSignalDirection
		{
			get { return previousSignalDirection; }
		}

		[Browsable(false)]
		public double PreviousIntentScore
		{
			get { return previousIntentScore; }
		}
		#endregion
	}
}

#if !STANDALONE_VERIFY
#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private IntentLayerV01[] cacheIntentLayerV01;
		public IntentLayerV01 IntentLayerV01(int volumeLookback, int rangeLookback, int structureLookback, int signalThreshold, double imbalanceVolumeSpikeThreshold, double absorptionVolumeSpikeThreshold, double absorptionWickThreshold, double sweepVolumeSpikeThreshold, double sweepWickThreshold, int breakoutExcursionTicks, int reclaimTicks, double imbalanceRatioThreshold, double absorptionDeltaThresholdRatio, double absorptionPriceEfficiencyThreshold, int minImbalanceVolumePerLevel, double imbalanceWeight, double absorptionWeight, double failedBreakoutWeight, double liquiditySweepWeight, double confluenceBonus, double expansiveVolumeBonus, double neutralityBuffer, bool showDebugPanel, bool highlightBars, bool enableTickStreaming, int streamPort)
		{
			return IntentLayerV01(Input, volumeLookback, rangeLookback, structureLookback, signalThreshold, imbalanceVolumeSpikeThreshold, absorptionVolumeSpikeThreshold, absorptionWickThreshold, sweepVolumeSpikeThreshold, sweepWickThreshold, breakoutExcursionTicks, reclaimTicks, imbalanceRatioThreshold, absorptionDeltaThresholdRatio, absorptionPriceEfficiencyThreshold, minImbalanceVolumePerLevel, imbalanceWeight, absorptionWeight, failedBreakoutWeight, liquiditySweepWeight, confluenceBonus, expansiveVolumeBonus, neutralityBuffer, showDebugPanel, highlightBars, enableTickStreaming, streamPort);
		}

		public IntentLayerV01 IntentLayerV01(ISeries<double> input, int volumeLookback, int rangeLookback, int structureLookback, int signalThreshold, double imbalanceVolumeSpikeThreshold, double absorptionVolumeSpikeThreshold, double absorptionWickThreshold, double sweepVolumeSpikeThreshold, double sweepWickThreshold, int breakoutExcursionTicks, int reclaimTicks, double imbalanceRatioThreshold, double absorptionDeltaThresholdRatio, double absorptionPriceEfficiencyThreshold, int minImbalanceVolumePerLevel, double imbalanceWeight, double absorptionWeight, double failedBreakoutWeight, double liquiditySweepWeight, double confluenceBonus, double expansiveVolumeBonus, double neutralityBuffer, bool showDebugPanel, bool highlightBars, bool enableTickStreaming, int streamPort)
		{
			if (cacheIntentLayerV01 != null)
				for (int idx = 0; idx < cacheIntentLayerV01.Length; idx++)
					if (cacheIntentLayerV01[idx] != null
						&& cacheIntentLayerV01[idx].VolumeLookback == volumeLookback
						&& cacheIntentLayerV01[idx].RangeLookback == rangeLookback
						&& cacheIntentLayerV01[idx].StructureLookback == structureLookback
						&& cacheIntentLayerV01[idx].SignalThreshold == signalThreshold
						&& cacheIntentLayerV01[idx].ImbalanceVolumeSpikeThreshold == imbalanceVolumeSpikeThreshold
						&& cacheIntentLayerV01[idx].AbsorptionVolumeSpikeThreshold == absorptionVolumeSpikeThreshold
						&& cacheIntentLayerV01[idx].AbsorptionWickThreshold == absorptionWickThreshold
						&& cacheIntentLayerV01[idx].SweepVolumeSpikeThreshold == sweepVolumeSpikeThreshold
						&& cacheIntentLayerV01[idx].SweepWickThreshold == sweepWickThreshold
						&& cacheIntentLayerV01[idx].BreakoutExcursionTicks == breakoutExcursionTicks
						&& cacheIntentLayerV01[idx].ReclaimTicks == reclaimTicks
						&& cacheIntentLayerV01[idx].ImbalanceRatioThreshold == imbalanceRatioThreshold
						&& cacheIntentLayerV01[idx].AbsorptionDeltaThresholdRatio == absorptionDeltaThresholdRatio
						&& cacheIntentLayerV01[idx].AbsorptionPriceEfficiencyThreshold == absorptionPriceEfficiencyThreshold
						&& cacheIntentLayerV01[idx].MinImbalanceVolumePerLevel == minImbalanceVolumePerLevel
						&& cacheIntentLayerV01[idx].ImbalanceWeight == imbalanceWeight
						&& cacheIntentLayerV01[idx].AbsorptionWeight == absorptionWeight
						&& cacheIntentLayerV01[idx].FailedBreakoutWeight == failedBreakoutWeight
						&& cacheIntentLayerV01[idx].LiquiditySweepWeight == liquiditySweepWeight
						&& cacheIntentLayerV01[idx].ConfluenceBonus == confluenceBonus
						&& cacheIntentLayerV01[idx].ExpansiveVolumeBonus == expansiveVolumeBonus
						&& cacheIntentLayerV01[idx].NeutralityBuffer == neutralityBuffer
						&& cacheIntentLayerV01[idx].ShowDebugPanel == showDebugPanel
						&& cacheIntentLayerV01[idx].HighlightBars == highlightBars
						&& cacheIntentLayerV01[idx].EnableTickStreaming == enableTickStreaming
						&& cacheIntentLayerV01[idx].StreamPort == streamPort
						&& cacheIntentLayerV01[idx].EqualsInput(input))
						return cacheIntentLayerV01[idx];
			return CacheIndicator<IntentLayerV01>(new IntentLayerV01()
			{
				VolumeLookback = volumeLookback,
				RangeLookback = rangeLookback,
				StructureLookback = structureLookback,
				SignalThreshold = signalThreshold,
				ImbalanceVolumeSpikeThreshold = imbalanceVolumeSpikeThreshold,
				AbsorptionVolumeSpikeThreshold = absorptionVolumeSpikeThreshold,
				AbsorptionWickThreshold = absorptionWickThreshold,
				SweepVolumeSpikeThreshold = sweepVolumeSpikeThreshold,
				SweepWickThreshold = sweepWickThreshold,
				BreakoutExcursionTicks = breakoutExcursionTicks,
				ReclaimTicks = reclaimTicks,
				ImbalanceRatioThreshold = imbalanceRatioThreshold,
				AbsorptionDeltaThresholdRatio = absorptionDeltaThresholdRatio,
				AbsorptionPriceEfficiencyThreshold = absorptionPriceEfficiencyThreshold,
				MinImbalanceVolumePerLevel = minImbalanceVolumePerLevel,
				ImbalanceWeight = imbalanceWeight,
				AbsorptionWeight = absorptionWeight,
				FailedBreakoutWeight = failedBreakoutWeight,
				LiquiditySweepWeight = liquiditySweepWeight,
				ConfluenceBonus = confluenceBonus,
				ExpansiveVolumeBonus = expansiveVolumeBonus,
				NeutralityBuffer = neutralityBuffer,
				ShowDebugPanel = showDebugPanel,
				HighlightBars = highlightBars,
				EnableTickStreaming = enableTickStreaming,
				StreamPort = streamPort
			}, input, ref cacheIntentLayerV01);
		}
	}
}

#endregion
#endif

