#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using Intent.Engine.Ingestion;
using Intent.Engine.Models;
using Intent.Engine.Signals;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.BarsTypes;
using NinjaTrader.NinjaScript.DrawingTools;
using System.Windows.Media;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
	public enum IntentExecutionMode
	{
		Manual = 0,
		Auto = 1
	}

	public class IntentAutoTraderV01 : Strategy
	{
		private const string LongSignalName = "IntentLong";
		private const string ShortSignalName = "IntentShort";
		private const string DashboardControlFileName = "intent-dashboard-control.txt";
		private const string DashboardStatusFileName = "intent-dashboard-status.json";
		private const string DashboardHeartbeatFileName = "intent-dashboard-heartbeat.txt";
		private const string DashboardTokenFileName = "intent-dashboard-token.txt";
		private const string TagCurrentPrice = "Intent.CurrentPrice";
		private const string TagEntryPrice = "Intent.EntryPrice";
		private const string TagStopPrice = "Intent.StopPrice";
		private const string TagTargetPrice = "Intent.TargetPrice";
		private const string TagSessionHigh = "Intent.SessionHigh";
		private const string TagSessionLow = "Intent.SessionLow";
		private const string TagVisualSummary = "Intent.VisualSummary";

		private IntentSignalEngine engine;
		private IntentSignalEngine higherTimeframeEngine;
		private IntentDirection previousSignalDirection;
		private double previousIntentScore;
		private IntentDirection previousHigherTimeframeSignalDirection;
		private double previousHigherTimeframeIntentScore;
		private int lastHigherTimeframeEvaluatedBar = -1;
		private string lastManualMessage = string.Empty;
		private DateTime tradingDate = Core.Globals.MinDate;
		private double sessionStartCumProfit;
		private int lastEntryBar = -1000000;
		private double sessionHigh;
		private double sessionLow;
		private double activeStopPrice;
		private double activeTargetPrice;
		private long lastProcessedCommandId;
		private long lastAppliedCommandId;
		private IntentExecutionMode? controlModeOverride;
		private bool dashboardExecutionEnabled = true;
		private string currentLockReason = "INITIALIZING";
		private int sessionTradeCount;
		private int currentCooldownRemainingBars;
		private bool lastCompressionPassed;
		private bool lastExpansionPassed;
		private string lastAttemptAction = "None";
		private string lastAttemptOutcome = "None";
		private string lastAttemptReason = string.Empty;
		private string lastAttemptTimestampUtc = string.Empty;
		private string lastOrderSummary = "None";
		private string lastExecutionSummary = "None";
		private string pendingDashboardCommand = string.Empty;
		private int pendingDashboardQuantity = 1;
		private int dashboardOrderQuantity = 1;
		private string lastCommandAcknowledgement = "None";
		private string lastAppliedCommandAction = "None";
		private DashboardBridge dashboardBridge;
		private int dashboardTimerInFlight;
		private SignalResult higherTimeframeAnalysis;
		private Timer dashboardCommandTimer;
		private IntentDirection activeRegimeDirection = IntentDirection.Neutral;
		private double activeRegimeStrength;
		private string activeRegimeSource = "NONE";
		private BarData lastHigherTimeframeBar;
		private IntentDirection higherTimeframeRegimeDirection = IntentDirection.Neutral;
		private double higherTimeframeRegimeStrength;
		private string higherTimeframeRegimeSource = "NONE";
		private int higherTimeframeOppositionBars;
		private int lastAutoSubmissionBar = -1000000;
		private TradeAction lastAutoSubmissionAction = TradeAction.StandAside;
		private bool suppressHistoricalStrategyPosition;
		private bool tradingHaltedForSession;
		private string haltReason = string.Empty;
		private Order pendingEntryOrder;
		private bool persistenceLoaded;
		private DateTime persistedDayDate = Core.Globals.MinDate;
		private double persistedDayPnL;
		private bool persistedHalted;

		protected override void OnStateChange()
		{
			LogDiagnostic("State=" + State.ToString());
			WriteStartupDiagnostics("state_change");

			if (State == State.SetDefaults)
			{
				Name = "IntentAutoTraderV01";
				Description = "Trades Intent engine signals with manual or auto execution modes.";
				// Decide and act on CLOSED bars. The engine's detectors (close location, wick ratios,
				// reclaim ticks, bar delta) are defined for completed bars; running them on a forming
				// bar (OnEachTick + barsAgo 0) repaints and makes live diverge from any bar-close
				// backtest. OnBarClose makes barsAgo 0 the just-closed bar. Dashboard commands stay
				// responsive via OnMarketData + the control timer.
				Calculate = Calculate.OnBarClose;
				EntriesPerDirection = 1;
				EntryHandling = EntryHandling.AllEntries;
				IsExitOnSessionCloseStrategy = true;
				ExitOnSessionCloseSeconds = 30;
				IsFillLimitOnTouch = false;
				MaximumBarsLookBack = MaximumBarsLookBack.TwoHundredFiftySix;
				OrderFillResolution = OrderFillResolution.Standard;
				Slippage = 1;
				StartBehavior = StartBehavior.WaitUntilFlat;
				TimeInForce = TimeInForce.Gtc;
				TraceOrders = false;
				RealtimeErrorHandling = RealtimeErrorHandling.StopCancelClose;
				StopTargetHandling = StopTargetHandling.PerEntryExecution;
				BarsRequiredToTrade = 20;

				ExecutionMode = IntentExecutionMode.Manual;
				AllowDashboardManualCommandsOutsideRealtime = true;
				AllowLongs = true;
				AllowShorts = true;
				AllowReversals = true;
				Quantity = 1;
				MaxContracts = 10;
				UseEngineStop = true;
				UseProfitTarget = true;
				RewardRiskMultiple = 1.50;
				MinimumStopDistanceTicks = 20;
				PrintManualSignals = true;
				DrawManualArrows = true;
				ManualArrowOffsetTicks = 2;
				CooldownBars = 0;
				// Capital protections default ON. The daily loss limit now includes open-position P&L,
				// latches a session halt on breach, and applies to manual/dashboard orders too.
				UseDailyLossLimit = true;
				MaxDailyLossCurrency = 200;
				UseFlatBeforeClose = true;
				FlatTime = 155500;
				EnableDashboardControl = true;
				DashboardBridgePort = 4110;
				UseHigherTimeframeFilter = true;
				HigherTimeframeMinutes = 5;
				MinHigherTimeframeIntentScore = 25;
				RegimeFlipOppositionBars = 2;
				ShowCurrentPriceLine = true;
				ShowTradeLevels = true;
				ShowSessionLevels = true;
				ShowVisualSummary = true;
				EnableChopFilter = false;
				CompressionRangeExpansionMax = 1.05;
				ExpansionRangeExpansionMin = 1.00;
				ExpansionVolumeSpikeMin = 0.95;
				MinAutoIntentScore = 45;
				TradeContinuationOnly = false;
				MaxTradesPerSession = 0;
				dashboardOrderQuantity = 1;

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
				BreakoutContinuationWeight = 0.15;
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
				ContradictionPenaltyFloorMultiplier = 0.30;
				ContradictionSuppressionFactor = 0.25;
				PriorSignalConfirmationBonus = 6;
				PriorSignalOppositionMultiplier = 0.85;
				BullishTrendStructureThreshold = 0.60;
				BearishTrendStructureThreshold = 0.40;
				ReversalCloseLocationThreshold = 0.55;
				BreakoutCloseThroughLevelTicks = 1;
				BreakoutVolumeSpikeThreshold = 1.10;
				ContinuationTradeThreshold = 58;
				ReversalTradeThreshold = 62;
				PullbackTradeThreshold = 65;
			}
			else if (State == State.DataLoaded)
			{
				engine = new IntentSignalEngine();
				higherTimeframeEngine = new IntentSignalEngine();
				StartDashboardCommandTimer();
				LogDiagnostic("DataLoaded engines initialized");
				WriteStartupDiagnostics("data_loaded");
			}
			else if (State == State.Configure)
			{
				if (UseHigherTimeframeFilter && HigherTimeframeMinutes > 0)
					// Volumetric series so the higher-timeframe bias filter runs on order flow, not the
					// degraded price-only fallback. If the data feed cannot supply volumetric data the
					// engine transparently falls back to the price-only path (same as before).
					AddVolumetric(Instrument.FullName, BarsPeriodType.Minute, HigherTimeframeMinutes, VolumetricDeltaType.BidAsk, 1);
				LogDiagnostic("Configure higherTimeframeFilter=" + UseHigherTimeframeFilter.ToString() + " minutes=" + HigherTimeframeMinutes.ToString(CultureInfo.InvariantCulture));
				WriteStartupDiagnostics("configure");
			}
			else if (State == State.Realtime)
			{
				suppressHistoricalStrategyPosition = Position != null && Position.MarketPosition != MarketPosition.Flat;
				if (suppressHistoricalStrategyPosition)
					lastCommandAcknowledgement = "Historical strategy position suppressed until live execution";
				LogDiagnostic("Realtime suppressHistoricalStrategyPosition=" + suppressHistoricalStrategyPosition.ToString());
				WriteStartupDiagnostics("realtime");
				WriteDashboardStatus(null, null);
			}
			else if (State == State.Terminated)
			{
				StopDashboardCommandTimer();
				LogDiagnostic("Terminated");
				WriteStartupDiagnostics("terminated");
				WriteDashboardStatus(null, null);
			}
		}

		protected override void OnBarUpdate()
		{
			bool commandChanged = ProcessDashboardControl();
			bool commandExecuted = TryExecutePendingDashboardCommand();
			if (commandChanged || commandExecuted)
				WriteStartupDiagnostics(commandExecuted ? "command_executed" : "command_processed");

			if (BarsInProgress > 1)
				return;

			if (BarsInProgress == 1)
			{
				if (!UseHigherTimeframeFilter || higherTimeframeEngine == null || CurrentBars.Length <= 1 || CurrentBars[1] < RequiredHigherTimeframeBars)
					return;

				if (CurrentBars[1] == lastHigherTimeframeEvaluatedBar)
					return;

				EngineSettings higherSettings = BuildSettings();
				BarData higherBar = BuildBarData(higherSettings, 1, higherTimeframeRegimeDirection, Math.Max(previousHigherTimeframeIntentScore, higherTimeframeRegimeStrength));
				lastHigherTimeframeBar = higherBar;
				higherTimeframeAnalysis = higherTimeframeEngine.Analyze(higherBar, higherSettings);
				previousHigherTimeframeSignalDirection = higherTimeframeAnalysis.Direction;
				previousHigherTimeframeIntentScore = higherTimeframeAnalysis.IntentScore;
				UpdateHigherTimeframeRegime(higherBar, higherTimeframeAnalysis);
				lastHigherTimeframeEvaluatedBar = CurrentBars[1];
				return;
			}

			if (CurrentBar < RequiredBars || engine == null)
			{
				WriteStartupDiagnostics("warmup");
				return;
			}

			EngineSettings settings = BuildSettings();
			BarData bar = BuildBarData(settings, 0, activeRegimeDirection, Math.Max(previousIntentScore, activeRegimeStrength));
			SignalResult analysis = engine.Analyze(bar, settings);

			UpdateSessionPnLTracking();
			previousSignalDirection = analysis.Direction;
			previousIntentScore = analysis.IntentScore;
			if (!UseHigherTimeframeFilter)
				UpdatePrimaryRegime(bar, analysis);
			currentLockReason = DetermineLockReason(bar, analysis);
			EnforceRiskHalts();

			WriteHeartbeat(bar, analysis);
			RenderVisuals(bar, analysis);
			WriteDashboardStatus(bar, analysis);

			if (GetEffectiveMode() == IntentExecutionMode.Manual)
			{
				UpdateAttemptState("None", "MANUAL", currentLockReason);
				HandleManualSignal(analysis);
				return;
			}

			if (!string.Equals(currentLockReason, "READY", StringComparison.Ordinal))
			{
				UpdateAttemptState(GetRecommendedActionLabel(analysis), "BLOCKED", currentLockReason);
				return;
			}

			HandleAutoSignal(bar, analysis);
		}

		protected override void OnMarketData(MarketDataEventArgs marketDataUpdate)
		{
			bool commandChanged = ProcessDashboardControl();
			bool commandExecuted = TryExecutePendingDashboardCommand();
			if (commandChanged || commandExecuted)
				WriteStartupDiagnostics(commandExecuted ? "command_executed" : "command_processed");
		}

		private void StartDashboardCommandTimer()
		{
			if (!EnableDashboardControl)
				return;

			if (dashboardBridge == null && DashboardBridgePort > 0)
			{
				dashboardBridge = new DashboardBridge(
					DashboardBridgePort,
					GetDashboardStatusPath(),
					Path.Combine(Path.GetTempPath(), DashboardTokenFileName));
				dashboardBridge.Start();
			}

			if (dashboardCommandTimer != null)
				return;

			dashboardCommandTimer = new Timer(
				state =>
				{
					// In-flight guard: never queue a second control cycle while one is pending on the
					// instrument thread, so callbacks cannot pile up behind a busy/slow tick.
					if (Interlocked.Exchange(ref dashboardTimerInFlight, 1) == 1)
						return;
					try
					{
						TriggerCustomEvent(
							ignored =>
							{
								try
								{
									bool commandChanged = ProcessDashboardControl();
									bool commandExecuted = TryExecutePendingDashboardCommand();
									if (commandChanged || commandExecuted)
										WriteStartupDiagnostics(commandExecuted ? "timer_command_executed" : "timer_command_processed");
								}
								finally
								{
									Interlocked.Exchange(ref dashboardTimerInFlight, 0);
								}
							},
							null);
					}
					catch
					{
						Interlocked.Exchange(ref dashboardTimerInFlight, 0);
					}
				},
				null,
				500,
				500);
		}

		private void StopDashboardCommandTimer()
		{
			if (dashboardCommandTimer != null)
			{
				try
				{
					dashboardCommandTimer.Dispose();
				}
				catch
				{
				}
				finally
				{
					dashboardCommandTimer = null;
				}
			}

			if (dashboardBridge != null)
			{
				try
				{
					dashboardBridge.Dispose();
				}
				catch
				{
				}
				finally
				{
					dashboardBridge = null;
				}
			}
		}

		private int RequiredBars
		{
			get { return Math.Max(StructureLookback, Math.Max(VolumeLookback, RangeLookback)); }
		}

		private int RequiredHigherTimeframeBars
		{
			get { return Math.Max(5, RequiredBars / 2); }
		}

		private IntentExecutionMode GetEffectiveMode()
		{
			return controlModeOverride.HasValue ? controlModeOverride.Value : ExecutionMode;
		}

		private void HandleManualSignal(SignalResult analysis)
		{
			if (!PrintManualSignals || analysis == null || analysis.RecommendedTradeAction == TradeAction.StandAside)
				return;

			DrawManualMarker(analysis);

			string message = string.Format(
				CultureInfo.InvariantCulture,
				"[Intent.Manual] {0:u} {1} action={2} score={3:0.##} style={4} stop={5} reason={6}",
				Time[0].ToUniversalTime(),
				Instrument == null ? string.Empty : Instrument.FullName,
				analysis.RecommendedTradeAction,
				analysis.IntentScore,
				analysis.EntryStyle,
				string.IsNullOrEmpty(analysis.StopLevel) ? "n/a" : analysis.StopLevel,
				analysis.DominantReason ?? string.Empty);

			if (string.Equals(message, lastManualMessage, StringComparison.Ordinal))
				return;

			lastManualMessage = message;
			Print(message);
		}

		private void DrawManualMarker(SignalResult analysis)
		{
			if (!DrawManualArrows || analysis == null)
				return;

			if (analysis.RecommendedTradeAction == TradeAction.Buy)
			{
				Draw.ArrowUp(
					this,
					"IntentManualBuy" + CurrentBar.ToString(CultureInfo.InvariantCulture),
					false,
					0,
					Low[0] - (ManualArrowOffsetTicks * TickSize),
					Brushes.LimeGreen);
			}
			else if (analysis.RecommendedTradeAction == TradeAction.Sell)
			{
				Draw.ArrowDown(
					this,
					"IntentManualSell" + CurrentBar.ToString(CultureInfo.InvariantCulture),
					false,
					0,
					High[0] + (ManualArrowOffsetTicks * TickSize),
					Brushes.IndianRed);
			}
		}

		private void HandleAutoSignal(BarData bar, SignalResult analysis)
		{
			if (analysis == null || bar == null)
				return;

			TradeAction effectiveAction = ResolveEffectiveTradeAction(bar, analysis);
			if (effectiveAction == TradeAction.StandAside)
				return;

			if (effectiveAction == TradeAction.Buy && !AllowLongs)
				return;

			if (effectiveAction == TradeAction.Sell && !AllowShorts)
				return;

			if (CurrentBar == lastAutoSubmissionBar && effectiveAction == lastAutoSubmissionAction)
				return;

			lastAutoSubmissionBar = CurrentBar;
			lastAutoSubmissionAction = effectiveAction;

			bool isLongSignal = effectiveAction == TradeAction.Buy;
			double stopPrice = ResolveStopPrice(bar, analysis, isLongSignal);
			double entryPrice = bar.Close;
			double targetPrice = BuildTargetPrice(isLongSignal, entryPrice, stopPrice);
			UpdateAttemptState(effectiveAction.ToString(), "SUBMITTING", BuildExecutionReason(analysis, effectiveAction));

			ConfigureRiskOrders(isLongSignal, stopPrice, targetPrice);

			if (Position.MarketPosition == MarketPosition.Flat)
			{
				SubmitEntry(isLongSignal, stopPrice, targetPrice);
				return;
			}

			if (!AllowReversals)
				return;

			if (isLongSignal && Position.MarketPosition == MarketPosition.Short)
			{
				ExitShort("IntentReverseToLong", ShortSignalName);
				SubmitEntry(true, stopPrice, targetPrice);
			}
			else if (!isLongSignal && Position.MarketPosition == MarketPosition.Long)
			{
				ExitLong("IntentReverseToShort", LongSignalName);
				SubmitEntry(false, stopPrice, targetPrice);
			}
		}

		private void UpdatePrimaryRegime(BarData bar, SignalResult analysis)
		{
			IntentDirection candidate = ResolveAnalysisBias(analysis);
			double regimeFloor = Math.Max(25, MinAutoIntentScore * 0.80);
			if (candidate == IntentDirection.Neutral)
			{
				activeRegimeStrength = Math.Max(0, activeRegimeStrength * 0.96);
				return;
			}

			if (activeRegimeDirection == IntentDirection.Neutral)
			{
				if (analysis.IntentScore < regimeFloor)
					return;

				activeRegimeDirection = candidate;
				activeRegimeStrength = analysis.IntentScore;
				activeRegimeSource = BuildRegimeSourceLabel(analysis);
				return;
			}

			if (candidate == activeRegimeDirection)
			{
				activeRegimeStrength = Math.Max(activeRegimeStrength * 0.90, analysis.IntentScore);
				activeRegimeSource = BuildRegimeSourceLabel(analysis);
				return;
			}

			if (!HasRegimeInvalidation(bar, analysis, activeRegimeDirection, candidate))
				return;

			activeRegimeDirection = candidate;
			activeRegimeStrength = Math.Max(regimeFloor, analysis.IntentScore);
			activeRegimeSource = BuildRegimeSourceLabel(analysis);
		}

		private void UpdateHigherTimeframeRegime(BarData bar, SignalResult analysis)
		{
			IntentDirection candidate = ResolveAnalysisBias(analysis);
			if (candidate == IntentDirection.Neutral)
			{
				higherTimeframeRegimeStrength = Math.Max(0, higherTimeframeRegimeStrength * 0.97);
				higherTimeframeOppositionBars = 0;
				return;
			}

			double regimeFloor = Math.Max(20, MinHigherTimeframeIntentScore);
			if (higherTimeframeRegimeDirection == IntentDirection.Neutral)
			{
				if (analysis.IntentScore < regimeFloor)
					return;

				higherTimeframeRegimeDirection = candidate;
				higherTimeframeRegimeStrength = analysis.IntentScore;
				higherTimeframeRegimeSource = BuildRegimeSourceLabel(analysis);
				higherTimeframeOppositionBars = 0;
				ApplyHigherTimeframeRegimeAsActive();
				return;
			}

			if (candidate == higherTimeframeRegimeDirection)
			{
				higherTimeframeRegimeStrength = Math.Max(higherTimeframeRegimeStrength * 0.92, analysis.IntentScore);
				higherTimeframeRegimeSource = BuildRegimeSourceLabel(analysis);
				higherTimeframeOppositionBars = 0;
				ApplyHigherTimeframeRegimeAsActive();
				return;
			}

			higherTimeframeOppositionBars++;
			if (!HasRegimeInvalidation(bar, analysis, higherTimeframeRegimeDirection, candidate) &&
				higherTimeframeOppositionBars < RegimeFlipOppositionBars)
				return;

			higherTimeframeRegimeDirection = candidate;
			higherTimeframeRegimeStrength = Math.Max(regimeFloor, analysis.IntentScore);
			higherTimeframeRegimeSource = BuildRegimeSourceLabel(analysis);
			higherTimeframeOppositionBars = 0;
			ApplyHigherTimeframeRegimeAsActive();
		}

		private void ApplyHigherTimeframeRegimeAsActive()
		{
			if (!UseHigherTimeframeFilter)
				return;

			activeRegimeDirection = higherTimeframeRegimeDirection;
			activeRegimeStrength = higherTimeframeRegimeStrength;
			activeRegimeSource = "HTF:" + higherTimeframeRegimeSource;
		}

		private IntentDirection ResolveAnalysisBias(SignalResult analysis)
		{
			if (analysis == null)
				return IntentDirection.Neutral;

			if (analysis.Direction != IntentDirection.Neutral)
				return analysis.Direction;

			if (analysis.TrendDirection != IntentDirection.Neutral)
				return analysis.TrendDirection;

			if (analysis.BullScore > analysis.BearScore + 1.0)
				return IntentDirection.Bullish;

			if (analysis.BearScore > analysis.BullScore + 1.0)
				return IntentDirection.Bearish;

			return IntentDirection.Neutral;
		}

		private IntentDirection ResolveActiveRegimeDirection()
		{
			if (UseHigherTimeframeFilter && higherTimeframeRegimeDirection != IntentDirection.Neutral)
				return higherTimeframeRegimeDirection;
			return activeRegimeDirection;
		}

		private double ResolveActiveRegimeStrength()
		{
			if (UseHigherTimeframeFilter && higherTimeframeRegimeDirection != IntentDirection.Neutral)
				return higherTimeframeRegimeStrength;
			return activeRegimeStrength;
		}

		private TradeAction ResolveEffectiveTradeAction(BarData bar, SignalResult analysis)
		{
			if (analysis == null)
				return TradeAction.StandAside;

			IntentDirection regimeDirection = ResolveActiveRegimeDirection();
			if (regimeDirection == IntentDirection.Neutral)
				return TradeAction.StandAside;

			if (analysis.RecommendedTradeAction == TradeAction.Buy && regimeDirection == IntentDirection.Bullish)
				return TradeAction.Buy;

			if (analysis.RecommendedTradeAction == TradeAction.Sell && regimeDirection == IntentDirection.Bearish)
				return TradeAction.Sell;

			if (regimeDirection == IntentDirection.Bullish && PassesBullishTrigger(bar, analysis))
				return TradeAction.Buy;

			if (regimeDirection == IntentDirection.Bearish && PassesBearishTrigger(bar, analysis))
				return TradeAction.Sell;

			return TradeAction.StandAside;
		}

		private bool PassesBullishTrigger(BarData bar, SignalResult analysis)
		{
			if (bar == null || analysis == null)
				return false;

			if (analysis.Direction == IntentDirection.Bearish &&
				analysis.SignalClassification == IntentSignalClassification.Reversal)
				return false;

			bool directionalPressure =
				analysis.Direction == IntentDirection.Bullish
				|| analysis.TrendDirection == IntentDirection.Bullish
				|| analysis.SignalClassification == IntentSignalClassification.Continuation
				|| analysis.SignalClassification == IntentSignalClassification.Pullback
				|| analysis.BullScore > analysis.BearScore;
			bool structureTrigger = bar.CloseLocation >= 0.52
				|| bar.IsBullishBody
				|| bar.ReclaimAboveLowTicks >= ReclaimTicks
				|| bar.BreakAboveTicks >= 1;
			bool scoreEdge = analysis.BullScore >= analysis.BearScore - 1.0;
			bool momentumReady = analysis.IntentScore >= GetEffectiveTriggerThreshold();

			return directionalPressure && structureTrigger && scoreEdge && momentumReady;
		}

		private bool PassesBearishTrigger(BarData bar, SignalResult analysis)
		{
			if (bar == null || analysis == null)
				return false;

			if (analysis.Direction == IntentDirection.Bullish &&
				analysis.SignalClassification == IntentSignalClassification.Reversal)
				return false;

			bool directionalPressure =
				analysis.Direction == IntentDirection.Bearish
				|| analysis.TrendDirection == IntentDirection.Bearish
				|| analysis.SignalClassification == IntentSignalClassification.Continuation
				|| analysis.SignalClassification == IntentSignalClassification.Pullback
				|| analysis.BearScore > analysis.BullScore;
			bool structureTrigger = bar.CloseLocation <= 0.48
				|| bar.IsBearishBody
				|| bar.ReclaimBelowHighTicks >= ReclaimTicks
				|| bar.BreakBelowTicks >= 1;
			bool scoreEdge = analysis.BearScore >= analysis.BullScore - 1.0;
			bool momentumReady = analysis.IntentScore >= GetEffectiveTriggerThreshold();

			return directionalPressure && structureTrigger && scoreEdge && momentumReady;
		}

		private bool HasRegimeInvalidation(BarData bar, SignalResult analysis, IntentDirection currentRegime, IntentDirection candidateRegime)
		{
			if (bar == null || analysis == null || currentRegime == IntentDirection.Neutral || candidateRegime == IntentDirection.Neutral)
				return false;

			double acceptanceBuffer = BreakoutCloseThroughLevelTicks * TickSize;
			bool structuralBreak = currentRegime == IntentDirection.Bullish
				? bar.Close <= bar.PriorSwingLow - acceptanceBuffer
				: bar.Close >= bar.PriorSwingHigh + acceptanceBuffer;
			bool reversalEvent = analysis.SignalClassification == IntentSignalClassification.Reversal
				&& analysis.Direction == candidateRegime;
			bool strongOpposition = analysis.IntentScore >= Math.Max(SignalThreshold, MinHigherTimeframeIntentScore)
				&& analysis.TrendDirection == candidateRegime;

			return structuralBreak || reversalEvent || strongOpposition;
		}

		private double GetEffectiveTriggerThreshold()
		{
			return Math.Max(25, MinAutoIntentScore * 0.80);
		}

		private double ResolveStopPrice(BarData bar, SignalResult analysis, bool isLongSignal)
		{
			double entryPrice = bar == null ? 0 : bar.Close;
			double minimumDistance = Math.Max(1, MinimumStopDistanceTicks) * TickSize;
			double parsed = ParsePrice(analysis == null ? string.Empty : analysis.StopLevel);
			if (parsed > 0 && entryPrice > 0)
			{
				double adjustedParsed = isLongSignal
					? Math.Min(parsed, entryPrice - minimumDistance)
					: Math.Max(parsed, entryPrice + minimumDistance);
				return Instrument != null && Instrument.MasterInstrument != null
					? Instrument.MasterInstrument.RoundToTickSize(adjustedParsed)
					: adjustedParsed;
			}

			if (bar == null)
				return 0;

			double fallbackStop = isLongSignal ? bar.PriorSwingLow : bar.PriorSwingHigh;
			if (UseHigherTimeframeFilter && lastHigherTimeframeBar != null)
				fallbackStop = isLongSignal ? lastHigherTimeframeBar.PriorSwingLow : lastHigherTimeframeBar.PriorSwingHigh;
			if (entryPrice > 0)
			{
				fallbackStop = isLongSignal
					? Math.Min(fallbackStop, entryPrice - minimumDistance)
					: Math.Max(fallbackStop, entryPrice + minimumDistance);
			}

			return Instrument != null && Instrument.MasterInstrument != null
				? Instrument.MasterInstrument.RoundToTickSize(fallbackStop)
				: fallbackStop;
		}

		private string BuildExecutionReason(SignalResult analysis, TradeAction effectiveAction)
		{
			string regime = ResolveActiveRegimeDirection().ToString();
			string source = UseHigherTimeframeFilter && higherTimeframeRegimeDirection != IntentDirection.Neutral
				? higherTimeframeRegimeSource
				: activeRegimeSource;
			return string.Format(
				CultureInfo.InvariantCulture,
				"{0} | regime={1} source={2} score={3:0.##} reason={4}",
				effectiveAction,
				regime,
				source,
				analysis == null ? 0 : analysis.IntentScore,
				analysis == null ? string.Empty : analysis.DominantReason ?? string.Empty);
		}

		private string BuildRegimeSourceLabel(SignalResult analysis)
		{
			if (analysis == null)
				return "NONE";

			return string.Format(
				CultureInfo.InvariantCulture,
				"{0}:{1}",
				analysis.SignalClassification,
				analysis.TrendDirection);
		}

		private void ConfigureRiskOrders(bool isLongSignal, double stopPrice, double targetPrice)
		{
			string signalName = isLongSignal ? LongSignalName : ShortSignalName;

			if (UseEngineStop && stopPrice > 0)
				SetStopLoss(signalName, CalculationMode.Price, stopPrice, false);

			if (UseProfitTarget && targetPrice > 0)
				SetProfitTarget(signalName, CalculationMode.Price, targetPrice);
		}

		private void SubmitEntry(bool isLongSignal, double stopPrice, double targetPrice)
		{
			activeStopPrice = stopPrice;
			activeTargetPrice = targetPrice;
			int quantity = ClampContracts(Quantity);
			// Session-trade count and cooldown are armed on FILL (OnOrderUpdate), not on submit.
			pendingEntryOrder = isLongSignal
				? EnterLong(quantity, LongSignalName)
				: EnterShort(quantity, ShortSignalName);
		}

		private void SubmitDashboardEntry(bool isLongSignal, int quantity)
		{
			int finalQuantity = ClampContracts(Math.Max(1, quantity));
			string signalName = isLongSignal ? "DashboardLong" : "DashboardShort";
			double refPrice = Close != null && Close.Count > 0 && CurrentBar >= 0 ? Close[0] : 0;
			double minimumDistance = Math.Max(1, MinimumStopDistanceTicks) * TickSize;
			double stopPrice = isLongSignal ? refPrice - minimumDistance : refPrice + minimumDistance;
			if (refPrice > 0 && Instrument != null && Instrument.MasterInstrument != null)
				stopPrice = Instrument.MasterInstrument.RoundToTickSize(stopPrice);
			double targetPrice = BuildTargetPrice(isLongSignal, refPrice, stopPrice);

			// A manual market entry is NEVER naked: always attach a protective stop (target if enabled),
			// bound to the dashboard signal name so the bracket actually attaches to this entry.
			if (refPrice > 0 && stopPrice > 0)
				SetStopLoss(signalName, CalculationMode.Price, stopPrice, false);
			if (UseProfitTarget && targetPrice > 0)
				SetProfitTarget(signalName, CalculationMode.Price, targetPrice);

			activeStopPrice = refPrice > 0 ? stopPrice : 0;
			activeTargetPrice = targetPrice;
			pendingEntryOrder = isLongSignal
				? EnterLong(finalQuantity, signalName)
				: EnterShort(finalQuantity, signalName);
		}

		protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice, int quantity, int filled, double averageFillPrice, OrderState orderState, DateTime time, ErrorCode error, string comment)
		{
			lastOrderSummary = string.Format(
				CultureInfo.InvariantCulture,
				"{0} {1} qty={2} filled={3} avg={4:0.00} err={5} {6}",
				time.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
				order == null ? orderState.ToString() : order.Name + " " + orderState,
				quantity,
				filled,
				averageFillPrice,
				error,
				string.IsNullOrWhiteSpace(comment) ? string.Empty : comment);

			// Count a trade and arm cooldown only when an ENTRY actually fills (not on submit), so a
			// rejected/never-filled entry cannot consume a session slot or block a later valid signal.
			if (order != null && pendingEntryOrder != null && object.ReferenceEquals(order, pendingEntryOrder) && orderState == OrderState.Filled)
			{
				lastEntryBar = CurrentBar;
				sessionTradeCount++;
				pendingEntryOrder = null;
				PersistDayState();
			}

			// Fail safe: if a protective stop/target is rejected while in a position, flatten and halt
			// rather than leave a live position the operator believes is protected.
			bool isProtectiveOrder = order != null && order.Name != null &&
				(order.Name.IndexOf("Stop", StringComparison.OrdinalIgnoreCase) >= 0
				 || order.Name.IndexOf("Profit", StringComparison.OrdinalIgnoreCase) >= 0
				 || order.Name.IndexOf("Target", StringComparison.OrdinalIgnoreCase) >= 0);
			if (orderState == OrderState.Rejected && isProtectiveOrder && Position.MarketPosition != MarketPosition.Flat)
				HaltTrading("PROTECTIVE_ORDER_REJECTED");

			if (error != ErrorCode.NoError || orderState == OrderState.Rejected)
				UpdateAttemptState(lastAttemptAction, "ORDER_REJECTED", lastOrderSummary);
			else if (orderState == OrderState.Working || orderState == OrderState.Accepted || orderState == OrderState.Submitted)
				UpdateAttemptState(lastAttemptAction, "ORDER_WORKING", lastOrderSummary);
			else if (orderState == OrderState.Filled)
				UpdateAttemptState(lastAttemptAction, "ORDER_FILLED", lastOrderSummary);
		}

		protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
		{
			lastExecutionSummary = string.Format(
				CultureInfo.InvariantCulture,
				"{0} {1} qty={2} px={3:0.00} pos={4}",
				time.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
				execution == null || execution.Order == null ? executionId : execution.Order.Name,
				quantity,
				price,
				marketPosition);
			if (State == State.Realtime)
				suppressHistoricalStrategyPosition = false;
			UpdateAttemptState(lastAttemptAction, "EXECUTED", lastExecutionSummary);
		}

		private void ExitForSessionClose()
		{
			// Unconditional whole-position close (handles any entry name, no residue after a reversal).
			FlattenAll();
		}

		private void UpdateSessionPnLTracking()
		{
			DateTime barDate = Time[0].Date;
			if (tradingDate == barDate)
			{
				sessionHigh = Math.Max(sessionHigh, High[0]);
				sessionLow = Math.Min(sessionLow, Low[0]);
				PersistDayState();
				return;
			}

			LoadPersistedDayStateIfNeeded();
			tradingDate = barDate;
			double instanceCumProfit = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
			if (persistedDayDate == barDate)
			{
				// Resume same-day accounting across a reload/recompile: rebase so realized day P&L
				// continues from the persisted value and restore a latched halt.
				sessionStartCumProfit = instanceCumProfit - persistedDayPnL;
				tradingHaltedForSession = persistedHalted;
				haltReason = persistedHalted ? "DAILY_LOSS_LIMIT" : string.Empty;
			}
			else
			{
				sessionStartCumProfit = instanceCumProfit;
				tradingHaltedForSession = false;
				haltReason = string.Empty;
			}

			sessionHigh = High[0];
			sessionLow = Low[0];
			sessionTradeCount = 0;
			PersistDayState();
		}

		private double GetCurrentSessionPnL()
		{
			return SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit - sessionStartCumProfit;
		}

		// Day P&L INCLUDING the open position's mark-to-market, so the daily loss limit engages on a
		// fast adverse move instead of only after the trade is realized.
		private double GetTotalSessionPnL()
		{
			double price = Close != null && Close.Count > 0 && CurrentBar >= 0 ? Close[0] : 0;
			double openPnL = Position.MarketPosition == MarketPosition.Flat ? 0 : GetUnrealizedPnL(price);
			return GetCurrentSessionPnL() + openPnL;
		}

		private int ClampContracts(int requested)
		{
			int cap = Math.Max(1, MaxContracts);
			return Math.Max(1, Math.Min(requested, cap));
		}

		// Unconditional flatten of the entire position regardless of which entry name opened it.
		// Name-scoped exits could leave residue after a reversal or a dashboard entry.
		private void FlattenAll()
		{
			if (Position.MarketPosition == MarketPosition.Long)
				ExitLong();
			else if (Position.MarketPosition == MarketPosition.Short)
				ExitShort();

			activeStopPrice = 0;
			activeTargetPrice = 0;
		}

		// Latched, session-wide kill: flattens and refuses all new entries (auto AND dashboard) until
		// a new session or manual re-arm. Survives reload via the persisted day state.
		private void HaltTrading(string reason)
		{
			bool firstHalt = !tradingHaltedForSession;
			tradingHaltedForSession = true;
			haltReason = string.IsNullOrEmpty(reason) ? "HALTED" : reason;
			dashboardExecutionEnabled = false;
			if (Position.MarketPosition != MarketPosition.Flat)
				FlattenAll();
			if (firstHalt)
			{
				UpdateAttemptState(lastAttemptAction, "HALTED", haltReason);
				lastCommandAcknowledgement = "Trading halted: " + haltReason;
			}
			PersistDayState();
		}

		private bool IsManualTradingBlocked(out string reason)
		{
			if (tradingHaltedForSession)
			{
				reason = "HALTED:" + haltReason;
				return true;
			}

			if (UseDailyLossLimit && GetTotalSessionPnL() <= -Math.Abs(MaxDailyLossCurrency))
			{
				reason = "DAILY_LOSS_LIMIT";
				return true;
			}

			if (UseFlatBeforeClose && State == State.Realtime && CurrentBar >= 0 && ToTime(Time[0]) >= FlatTime)
			{
				reason = "PAST_FLAT_TIME";
				return true;
			}

			reason = string.Empty;
			return false;
		}

		// Applies the hard safety lock reasons by actually flattening/latching, not just gating entries.
		private void EnforceRiskHalts()
		{
			if (string.Equals(currentLockReason, "DAILY_LOSS_LIMIT", StringComparison.Ordinal))
			{
				HaltTrading("DAILY_LOSS_LIMIT");
				return;
			}

			if (string.Equals(currentLockReason, "HALTED", StringComparison.Ordinal))
			{
				if (Position.MarketPosition != MarketPosition.Flat)
					FlattenAll();
				return;
			}

			if (string.Equals(currentLockReason, "PAST_FLAT_TIME", StringComparison.Ordinal) && Position.MarketPosition != MarketPosition.Flat)
				ExitForSessionClose();
		}

		private string GetRiskStatePath()
		{
			string instrument = Instrument == null || string.IsNullOrEmpty(Instrument.FullName) ? "default" : Instrument.FullName;
			foreach (char invalid in Path.GetInvalidFileNameChars())
				instrument = instrument.Replace(invalid, '_');
			return Path.Combine(Path.GetTempPath(), "intent-riskstate-" + instrument + ".txt");
		}

		// Persist the day's realized P&L + halt latch so a mid-session reload/recompile does not forget
		// prior losses and re-arm a halted account.
		private void PersistDayState()
		{
			if (!UseDailyLossLimit)
				return;

			try
			{
				string line = string.Format(
					CultureInfo.InvariantCulture,
					"{0:yyyy-MM-dd}|{1:0.########}|{2}",
					tradingDate,
					GetCurrentSessionPnL(),
					tradingHaltedForSession ? "1" : "0");
				File.WriteAllText(GetRiskStatePath(), line, Encoding.UTF8);
			}
			catch
			{
			}
		}

		private void LoadPersistedDayStateIfNeeded()
		{
			if (persistenceLoaded)
				return;

			persistenceLoaded = true;
			if (!UseDailyLossLimit)
				return;

			try
			{
				string path = GetRiskStatePath();
				if (!File.Exists(path))
					return;

				string[] parts = File.ReadAllText(path, Encoding.UTF8).Split('|');
				if (parts.Length < 3)
					return;

				DateTime persistedDate;
				if (DateTime.TryParseExact(parts[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out persistedDate))
					persistedDayDate = persistedDate.Date;

				double persistedPnL;
				if (double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out persistedPnL))
					persistedDayPnL = persistedPnL;

				persistedHalted = parts[2].Trim() == "1";
			}
			catch
			{
			}
		}

		private string DetermineLockReason(BarData bar, SignalResult analysis)
		{
			currentCooldownRemainingBars = CooldownBars > 0 ? Math.Max(0, (lastEntryBar + CooldownBars) - CurrentBar) : 0;
			lastCompressionPassed = false;
			lastExpansionPassed = false;

			if (CurrentBar < RequiredBars || engine == null)
				return "WARMUP";

			// Hard safety halts evaluated in ALL modes (manual + auto) BEFORE any mode-specific gating,
			// so a daily-loss breach, a latched halt, or the flat-before-close window also stops manual
			// and dashboard-initiated trading — not just auto entries.
			if (tradingHaltedForSession)
				return "HALTED";

			if (UseDailyLossLimit && GetTotalSessionPnL() <= -Math.Abs(MaxDailyLossCurrency))
				return "DAILY_LOSS_LIMIT";

			if (UseFlatBeforeClose && State == State.Realtime && ToTime(Time[0]) >= FlatTime)
				return "PAST_FLAT_TIME";

			if (GetEffectiveMode() == IntentExecutionMode.Manual)
				return dashboardExecutionEnabled ? "MANUAL_MODE" : "MANUAL_LOCKED";

			if (State != State.Realtime)
				return "WAITING_REALTIME";

			if (suppressHistoricalStrategyPosition)
				return "HISTORICAL_POSITION_SUPPRESSED";

			if (EnableDashboardControl && !dashboardExecutionEnabled)
				return "EXECUTION_DISABLED";

			if (CooldownBars > 0 && CurrentBar <= lastEntryBar + CooldownBars)
				return "COOLDOWN";

			if (MaxTradesPerSession > 0 && sessionTradeCount >= MaxTradesPerSession)
				return "MAX_TRADES_SESSION";

			if (analysis == null || bar == null)
				return "NO_ANALYSIS";

			if (UseHigherTimeframeFilter)
			{
				string higherTimeframeLockReason = DetermineHigherTimeframeLockReason(bar, analysis);
				if (!string.IsNullOrEmpty(higherTimeframeLockReason))
					return higherTimeframeLockReason;
			}

			TradeAction effectiveAction = ResolveEffectiveTradeAction(bar, analysis);
			if (effectiveAction == TradeAction.StandAside)
				return ResolveActiveRegimeDirection() == IntentDirection.Neutral ? "STAND_ASIDE" : "WAITING_TRIGGER";

			IntentDirection regime = ResolveActiveRegimeDirection();
			if (analysis.SignalClassification == IntentSignalClassification.Reversal && regime != IntentDirection.Neutral)
			{
				bool reversalAlignsWithRegime =
					(regime == IntentDirection.Bullish && effectiveAction == TradeAction.Buy) ||
					(regime == IntentDirection.Bearish && effectiveAction == TradeAction.Sell);
				if (!reversalAlignsWithRegime)
					return "COUNTER_REGIME";
			}

			if (TradeContinuationOnly && analysis.SignalClassification != IntentSignalClassification.Continuation)
				return "CONTINUATION_ONLY";

			if (EnableChopFilter && !PassesCompressionExpansionGate(bar))
				return "CHOP_FILTER";

			return "READY";
		}

		private string DetermineHigherTimeframeLockReason(BarData bar, SignalResult analysis)
		{
			if (CurrentBars.Length <= 1 || CurrentBars[1] < RequiredHigherTimeframeBars || higherTimeframeAnalysis == null)
				return "HTF_WAIT";

			if (higherTimeframeRegimeDirection == IntentDirection.Neutral)
				return "HTF_NEUTRAL";

			if (analysis == null)
				return "HTF_WAIT";

			TradeAction effectiveAction = ResolveEffectiveTradeAction(bar, analysis);
			if (effectiveAction == TradeAction.Buy && higherTimeframeRegimeDirection != IntentDirection.Bullish)
				return "HTF_MISMATCH";

			if (effectiveAction == TradeAction.Sell && higherTimeframeRegimeDirection != IntentDirection.Bearish)
				return "HTF_MISMATCH";

			return string.Empty;
		}

		private bool PassesCompressionExpansionGate(BarData bar)
		{
			if (bar == null)
				return false;

			if (CurrentBar < 1)
				return false;

			double priorAverageRange = AverageRange(RangeLookback, 1, 0);
			double priorRange = Math.Max(High[1] - Low[1], TickSize);
			double priorRangeExpansion = priorAverageRange <= 0 ? 0 : priorRange / priorAverageRange;
			bool compressed = priorRangeExpansion <= CompressionRangeExpansionMax;
			bool expanded = bar.RangeExpansion >= ExpansionRangeExpansionMin && bar.VolumeSpike >= ExpansionVolumeSpikeMin;
			lastCompressionPassed = compressed;
			lastExpansionPassed = expanded;
			return compressed && expanded;
		}

		private bool ProcessDashboardControl()
		{
			if (!EnableDashboardControl)
				return false;

			string raw = ReadDashboardCommand();
			if (string.IsNullOrWhiteSpace(raw))
				return false;

			// The control channel may deliver more than one command at once (newline-separated). Process
			// each one so rapidly-issued commands are not dropped; per-command id dedup prevents re-runs.
			bool any = false;
			string[] lines = raw.Split('\n');
			for (int index = 0; index < lines.Length; index++)
			{
				string line = lines[index].Trim();
				if (line.Length > 0 && ProcessSingleCommand(line))
					any = true;
			}

			return any;
		}

		private bool ProcessSingleCommand(string command)
		{
			long commandId = ParseCommandId(command);
			if (commandId <= 0 || commandId == lastProcessedCommandId)
				return false;

			lastProcessedCommandId = commandId;
			string action = ParseCommandValue(command, "action");
			string value = ParseCommandValue(command, "value");

			if (string.Equals(action, "set_mode", StringComparison.OrdinalIgnoreCase))
			{
				if (string.Equals(value, "auto", StringComparison.OrdinalIgnoreCase))
					controlModeOverride = IntentExecutionMode.Auto;
				else if (string.Equals(value, "manual", StringComparison.OrdinalIgnoreCase))
					controlModeOverride = IntentExecutionMode.Manual;
				lastAppliedCommandId = commandId;
				lastAppliedCommandAction = action;
			}
			else if (string.Equals(action, "set_execution", StringComparison.OrdinalIgnoreCase))
			{
				dashboardExecutionEnabled = string.Equals(value, "enabled", StringComparison.OrdinalIgnoreCase);
				lastAppliedCommandId = commandId;
				lastAppliedCommandAction = action;
			}
			else if (string.Equals(action, "set_continuation_only", StringComparison.OrdinalIgnoreCase))
			{
				TradeContinuationOnly = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
				lastAppliedCommandId = commandId;
				lastAppliedCommandAction = action;
			}
			else if (string.Equals(action, "update_rules", StringComparison.OrdinalIgnoreCase))
			{
				MaxTradesPerSession = ParseIntOrDefault(ParseCommandValue(command, "max_trades_per_session"), MaxTradesPerSession, 0);
				CooldownBars = ParseIntOrDefault(ParseCommandValue(command, "cooldown_bars"), CooldownBars, 0);
				MinAutoIntentScore = ParseDoubleOrDefault(ParseCommandValue(command, "min_auto_intent_score"), MinAutoIntentScore, 1.0);
				CompressionRangeExpansionMax = ParseDoubleOrDefault(ParseCommandValue(command, "compression_range_expansion_max"), CompressionRangeExpansionMax, 0.1);
				ExpansionRangeExpansionMin = ParseDoubleOrDefault(ParseCommandValue(command, "expansion_range_expansion_min"), ExpansionRangeExpansionMin, 0.5);
				ExpansionVolumeSpikeMin = ParseDoubleOrDefault(ParseCommandValue(command, "expansion_volume_spike_min"), ExpansionVolumeSpikeMin, 0.5);
				lastAppliedCommandId = commandId;
				lastAppliedCommandAction = action;
			}
			else if (string.Equals(action, "flatten", StringComparison.OrdinalIgnoreCase))
			{
				pendingDashboardCommand = "flatten";
				lastAppliedCommandId = commandId;
				lastAppliedCommandAction = action;
			}
			else if (string.Equals(action, "buy_market", StringComparison.OrdinalIgnoreCase))
			{
				pendingDashboardCommand = "buy_market";
				pendingDashboardQuantity = ParseIntOrDefault(ParseCommandValue(command, "quantity"), dashboardOrderQuantity, 1);
				lastAppliedCommandId = commandId;
				lastAppliedCommandAction = action;
			}
			else if (string.Equals(action, "sell_market", StringComparison.OrdinalIgnoreCase))
			{
				pendingDashboardCommand = "sell_market";
				pendingDashboardQuantity = ParseIntOrDefault(ParseCommandValue(command, "quantity"), dashboardOrderQuantity, 1);
				lastAppliedCommandId = commandId;
				lastAppliedCommandAction = action;
			}
			else if (string.Equals(action, "reverse", StringComparison.OrdinalIgnoreCase))
			{
				pendingDashboardCommand = "reverse";
				pendingDashboardQuantity = ParseIntOrDefault(ParseCommandValue(command, "quantity"), dashboardOrderQuantity, 1);
				lastAppliedCommandId = commandId;
				lastAppliedCommandAction = action;
			}
			else if (string.Equals(action, "set_dashboard_quantity", StringComparison.OrdinalIgnoreCase))
			{
				dashboardOrderQuantity = ParseIntOrDefault(ParseCommandValue(command, "quantity"), dashboardOrderQuantity, 1);
				lastCommandAcknowledgement = "Quantity set to " + dashboardOrderQuantity.ToString(CultureInfo.InvariantCulture);
				lastAppliedCommandId = commandId;
				lastAppliedCommandAction = action;
			}

			return true;
		}

		private string ReadDashboardCommand()
		{
			// Non-blocking: the background DashboardBridge owns all HTTP I/O; the instrument thread only
			// reads the last command it fetched. Falls back to the local temp-file control channel.
			if (dashboardBridge != null)
			{
				string bridgeCommand = dashboardBridge.LatestCommand;
				if (!string.IsNullOrWhiteSpace(bridgeCommand))
					return bridgeCommand;
			}

			string path = GetDashboardControlPath();
			if (!File.Exists(path))
				return string.Empty;

			try
			{
				return File.ReadAllText(path, Encoding.UTF8);
			}
			catch
			{
				return string.Empty;
			}
		}

		// Owns ALL dashboard HTTP I/O on a dedicated background thread so the NinjaTrader instrument
		// thread never blocks on a slow/dead bridge (previously a synchronous GET/POST per tick).
		// Sends a per-session token header (read from a user-temp file the local console writes) so the
		// command/status endpoints can reject unauthenticated callers.
		private sealed class DashboardBridge : IDisposable
		{
			private readonly int port;
			private readonly string statusFilePath;
			private readonly string tokenFilePath;
			private readonly Thread worker;
			private readonly object gate = new object();
			private volatile bool stop;
			private volatile string latestCommand = string.Empty;
			private string pendingStatus;

			public DashboardBridge(int port, string statusFilePath, string tokenFilePath)
			{
				this.port = port;
				this.statusFilePath = statusFilePath;
				this.tokenFilePath = tokenFilePath;
				worker = new Thread(Loop);
				worker.IsBackground = true;
				worker.Name = "IntentDashboardBridge";
			}

			public string LatestCommand
			{
				get { return latestCommand; }
			}

			public void Start()
			{
				if (port > 0)
					worker.Start();
			}

			public void EnqueueStatus(string json)
			{
				if (string.IsNullOrWhiteSpace(json))
					return;
				lock (gate)
					pendingStatus = json;
			}

			public void Dispose()
			{
				stop = true;
				try
				{
					if (worker != null && worker.IsAlive)
						worker.Join(500);
				}
				catch
				{
				}
			}

			private void Loop()
			{
				while (!stop)
				{
					try
					{
						string status;
						lock (gate)
						{
							status = pendingStatus;
							pendingStatus = null;
						}

						if (status != null && !Post("/api/strategy-status", status))
							WriteStatusFile(status);

						string command = Get("/api/command");
						if (command != null)
							latestCommand = command;
					}
					catch
					{
					}

					for (int i = 0; i < 12 && !stop; i++)
						Thread.Sleep(10);
				}
			}

			private string Url(string path)
			{
				return "http://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture) + path;
			}

			private void ApplyToken(HttpWebRequest request)
			{
				try
				{
					if (string.IsNullOrEmpty(tokenFilePath) || !File.Exists(tokenFilePath))
						return;
					string token = File.ReadAllText(tokenFilePath, Encoding.UTF8).Trim();
					if (token.Length > 0)
						request.Headers["X-Intent-Token"] = token;
				}
				catch
				{
				}
			}

			private string Get(string path)
			{
				try
				{
					HttpWebRequest request = (HttpWebRequest)WebRequest.Create(Url(path));
					request.Method = "GET";
					request.Timeout = 250;
					request.ReadWriteTimeout = 250;
					request.Proxy = null;
					ApplyToken(request);
					using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
					using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
						return reader.ReadToEnd();
				}
				catch
				{
					return null;
				}
			}

			private bool Post(string path, string payload)
			{
				try
				{
					byte[] bytes = Encoding.UTF8.GetBytes(payload);
					HttpWebRequest request = (HttpWebRequest)WebRequest.Create(Url(path));
					request.Method = "POST";
					request.ContentType = "application/json; charset=utf-8";
					request.ContentLength = bytes.Length;
					request.Timeout = 250;
					request.ReadWriteTimeout = 250;
					request.Proxy = null;
					ApplyToken(request);
					using (Stream requestStream = request.GetRequestStream())
						requestStream.Write(bytes, 0, bytes.Length);
					using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
					{
					}
					return true;
				}
				catch
				{
					return false;
				}
			}

			private void WriteStatusFile(string payload)
			{
				if (string.IsNullOrEmpty(statusFilePath))
					return;
				try
				{
					string tempPath = statusFilePath + ".tmp";
					File.WriteAllText(tempPath, payload, Encoding.UTF8);
					try { File.Delete(statusFilePath); } catch { }
					File.Move(tempPath, statusFilePath);
				}
				catch
				{
				}
			}
		}

		private bool TryExecutePendingDashboardCommand()
		{
			if (string.IsNullOrWhiteSpace(pendingDashboardCommand))
				return false;

			if (!AllowDashboardManualCommandsOutsideRealtime && State != State.Realtime)
			{
				lastCommandAcknowledgement = "Command blocked: WAITING_REALTIME";
				return true;
			}

			if (!dashboardExecutionEnabled)
			{
				lastCommandAcknowledgement = "Command blocked: EXECUTION_DISABLED";
				return true;
			}

			string command = pendingDashboardCommand;
			int quantity = Math.Max(1, pendingDashboardQuantity);
			pendingDashboardCommand = string.Empty;

			// Flatten is always allowed (it reduces risk). New manual entries (buy/sell/reverse) obey the
			// same hard safety halts as auto: a latched halt, a daily-loss breach, or past flat time.
			string manualBlockReason;
			if (!string.Equals(command, "flatten", StringComparison.OrdinalIgnoreCase) && IsManualTradingBlocked(out manualBlockReason))
			{
				lastCommandAcknowledgement = "Command blocked: " + manualBlockReason;
				UpdateAttemptState("Dashboard", "BLOCKED", manualBlockReason);
				return true;
			}

			if (string.Equals(command, "flatten", StringComparison.OrdinalIgnoreCase))
			{
				ExitForSessionClose();
				lastCommandAcknowledgement = "Flatten submitted";
				UpdateAttemptState("Flatten", "SUBMITTING", "Dashboard flatten command");
				return true;
			}

			if (string.Equals(command, "buy_market", StringComparison.OrdinalIgnoreCase))
			{
				SubmitDashboardEntry(true, quantity);
				lastCommandAcknowledgement = "Buy market submitted";
				UpdateAttemptState("DashboardBuy", "SUBMITTING", "Dashboard buy market command");
				return true;
			}

			if (string.Equals(command, "sell_market", StringComparison.OrdinalIgnoreCase))
			{
				SubmitDashboardEntry(false, quantity);
				lastCommandAcknowledgement = "Sell market submitted";
				UpdateAttemptState("DashboardSell", "SUBMITTING", "Dashboard sell market command");
				return true;
			}

			if (string.Equals(command, "reverse", StringComparison.OrdinalIgnoreCase))
			{
				if (Position.MarketPosition == MarketPosition.Long)
				{
					ExitLong("DashboardReverseExit", LongSignalName);
					ExitLong("DashboardReverseExitDash", "DashboardLong");
					SubmitDashboardEntry(false, quantity);
				}
				else if (Position.MarketPosition == MarketPosition.Short)
				{
					ExitShort("DashboardReverseExit", ShortSignalName);
					ExitShort("DashboardReverseExitDash", "DashboardShort");
					SubmitDashboardEntry(true, quantity);
				}
				else
				{
					SubmitDashboardEntry(true, quantity);
				}

				lastCommandAcknowledgement = "Reverse submitted";
				UpdateAttemptState("DashboardReverse", "SUBMITTING", "Dashboard reverse command");
			}

			return true;
		}

		private void RenderVisuals(BarData bar, SignalResult analysis)
		{
			if (bar == null)
				return;

			double displayCurrentPrice = bar.Close;
			double displayEntryPrice = Position.MarketPosition == MarketPosition.Flat ? 0 : Position.AveragePrice;
			double displayStopPrice = Position.MarketPosition == MarketPosition.Flat ? ParsePrice(analysis == null ? string.Empty : analysis.StopLevel) : activeStopPrice;
			bool isLongPlan = analysis != null && analysis.RecommendedTradeAction == TradeAction.Buy;
			double displayTargetPrice = Position.MarketPosition == MarketPosition.Flat
				? BuildTargetPrice(isLongPlan, displayCurrentPrice, displayStopPrice)
				: activeTargetPrice;

			if (ShowCurrentPriceLine)
				DrawPriceMarker(TagCurrentPrice, "PX", displayCurrentPrice, Brushes.Gold);
			else
				RemoveDrawObject(TagCurrentPrice);

			if (ShowTradeLevels && displayEntryPrice > 0)
				DrawPriceMarker(TagEntryPrice, "ENT", displayEntryPrice, Brushes.DeepSkyBlue);
			else
				RemoveDrawObject(TagEntryPrice);

			if (ShowTradeLevels && displayStopPrice > 0)
				DrawPriceMarker(TagStopPrice, "STP", displayStopPrice, Brushes.IndianRed);
			else
				RemoveDrawObject(TagStopPrice);

			if (ShowTradeLevels && displayTargetPrice > 0)
				DrawPriceMarker(TagTargetPrice, "TGT", displayTargetPrice, Brushes.MediumSeaGreen);
			else
				RemoveDrawObject(TagTargetPrice);

			if (ShowSessionLevels)
			{
				DrawPriceMarker(TagSessionHigh, "HOD", sessionHigh, Brushes.DarkOrange);
				DrawPriceMarker(TagSessionLow, "LOD", sessionLow, Brushes.DodgerBlue);
			}
			else
			{
				RemoveDrawObject(TagSessionHigh);
				RemoveDrawObject(TagSessionLow);
			}

			if (ShowVisualSummary)
			{
				string summary = string.Format(
					CultureInfo.InvariantCulture,
					"Mode: {0}  Exec: {1}\nPos: {2}\nPx: {3:0.00}  Entry: {4}\nStop: {5}  Target: {6}\nSess H/L: {7:0.00} / {8:0.00}",
					GetEffectiveMode(),
					dashboardExecutionEnabled ? "On" : "Off",
					Position.MarketPosition,
					displayCurrentPrice,
					displayEntryPrice > 0 ? displayEntryPrice.ToString("0.00", CultureInfo.InvariantCulture) : "n/a",
					displayStopPrice > 0 ? displayStopPrice.ToString("0.00", CultureInfo.InvariantCulture) : "n/a",
					displayTargetPrice > 0 ? displayTargetPrice.ToString("0.00", CultureInfo.InvariantCulture) : "n/a",
					sessionHigh,
					sessionLow);
				summary += "\nLock: " + currentLockReason;
				summary += string.Format(
					CultureInfo.InvariantCulture,
					"\nCD: {0}  Cmp: {1}  Exp: {2}  Trd: {3}",
					currentCooldownRemainingBars,
					lastCompressionPassed ? "Y" : "N",
					lastExpansionPassed ? "Y" : "N",
					sessionTradeCount);
				if (UseHigherTimeframeFilter)
				{
					summary += string.Format(
						CultureInfo.InvariantCulture,
						"\nHTF {0}m: {1} {2:0.0}",
						HigherTimeframeMinutes,
						higherTimeframeAnalysis == null ? "WAIT" : higherTimeframeAnalysis.Direction.ToString(),
						higherTimeframeAnalysis == null ? 0 : higherTimeframeAnalysis.IntentScore);
				}
				Draw.TextFixed(this, TagVisualSummary, summary, TextPosition.TopRight, Brushes.Gainsboro, new SimpleFont("Consolas", 12), Brushes.Black, Brushes.DimGray, 35);
			}
			else
			{
				RemoveDrawObject(TagVisualSummary);
			}
		}

		private void DrawPriceMarker(string tag, string prefix, double price, Brush brush)
		{
			Draw.Text(
				this,
				tag,
				false,
				prefix + " " + price.ToString("0.00", CultureInfo.InvariantCulture),
				0,
				price,
				0,
				brush,
				new SimpleFont("Consolas", 12),
				System.Windows.TextAlignment.Left,
				Brushes.Transparent,
				Brushes.Transparent,
				0);
		}

		private void WriteDashboardStatus(BarData bar, SignalResult analysis)
		{
			if (!EnableDashboardControl)
				return;

			double currentPrice = bar == null ? 0 : bar.Close;
			MarketPosition displayPosition = GetDisplayPosition();
			bool displayFlat = displayPosition == MarketPosition.Flat;
			double entryPrice = displayFlat ? 0 : Position.AveragePrice;
			double stopPrice = displayFlat ? ParsePrice(analysis == null ? string.Empty : analysis.StopLevel) : activeStopPrice;
			bool isLongPlan = analysis != null && analysis.RecommendedTradeAction == TradeAction.Buy;
			double targetPrice = displayFlat
				? BuildTargetPrice(isLongPlan, currentPrice, stopPrice)
				: activeTargetPrice;

			StringBuilder builder = new StringBuilder(512);
			builder.Append("{");
			AppendJson(builder, "connected", State != State.Terminated);
			AppendJson(builder, "mode", GetEffectiveMode().ToString());
			AppendJson(builder, "executionEnabled", dashboardExecutionEnabled);
			AppendJson(builder, "position", displayPosition.ToString());
			AppendJson(builder, "positionSource", suppressHistoricalStrategyPosition ? "historical_suppressed" : "live_strategy");
			AppendJson(builder, "timeframeMode", GetTimeframeModeLabel());
			AppendJson(builder, "higherTimeframeMinutes", HigherTimeframeMinutes);
			AppendJson(builder, "higherTimeframeDirection", higherTimeframeAnalysis == null ? string.Empty : higherTimeframeAnalysis.Direction.ToString());
			AppendJson(builder, "higherTimeframeIntentScore", higherTimeframeAnalysis == null ? 0 : higherTimeframeAnalysis.IntentScore);
			AppendJson(builder, "higherTimeframeTradeAction", higherTimeframeAnalysis == null ? string.Empty : higherTimeframeAnalysis.RecommendedTradeAction.ToString());
			AppendJson(builder, "higherTimeframeReason", higherTimeframeAnalysis == null ? string.Empty : higherTimeframeAnalysis.DominantReason);
			AppendJson(builder, "regimeDirection", activeRegimeDirection.ToString());
			AppendJson(builder, "regimeStrength", activeRegimeStrength);
			AppendJson(builder, "regimeSource", activeRegimeSource);
			AppendJson(builder, "higherTimeframeRegimeDirection", higherTimeframeRegimeDirection.ToString());
			AppendJson(builder, "higherTimeframeRegimeStrength", higherTimeframeRegimeStrength);
			AppendJson(builder, "higherTimeframeRegimeSource", higherTimeframeRegimeSource);
			AppendJson(builder, "currentPrice", currentPrice);
			AppendJson(builder, "entryPrice", entryPrice);
			AppendJson(builder, "stopPrice", stopPrice);
			AppendJson(builder, "targetPrice", targetPrice);
			AppendJson(builder, "sessionHigh", sessionHigh);
			AppendJson(builder, "sessionLow", sessionLow);
			AppendJson(builder, "sessionPnl", GetCurrentSessionPnL());
			AppendJson(builder, "sessionTradeCount", sessionTradeCount);
			AppendJson(builder, "cooldownRemainingBars", currentCooldownRemainingBars);
			AppendJson(builder, "compressionPassed", lastCompressionPassed);
			AppendJson(builder, "expansionPassed", lastExpansionPassed);
			AppendJson(builder, "tradeContinuationOnly", TradeContinuationOnly);
			AppendJson(builder, "maxTradesPerSession", MaxTradesPerSession);
			AppendJson(builder, "cooldownBars", CooldownBars);
			AppendJson(builder, "minAutoIntentScore", MinAutoIntentScore);
			AppendJson(builder, "compressionRangeExpansionMax", CompressionRangeExpansionMax);
			AppendJson(builder, "expansionRangeExpansionMin", ExpansionRangeExpansionMin);
			AppendJson(builder, "expansionVolumeSpikeMin", ExpansionVolumeSpikeMin);
			AppendJson(builder, "useHigherTimeframeFilter", UseHigherTimeframeFilter);
			AppendJson(builder, "minHigherTimeframeIntentScore", MinHigherTimeframeIntentScore);
			AppendJson(builder, "direction", analysis == null ? string.Empty : analysis.Direction.ToString());
			AppendJson(builder, "trendDirection", analysis == null ? string.Empty : analysis.TrendDirection.ToString());
			AppendJson(builder, "signalClassification", analysis == null ? string.Empty : analysis.SignalClassification.ToString());
			AppendJson(builder, "effectiveTradeAction", ResolveEffectiveTradeAction(bar, analysis).ToString());
			AppendJson(builder, "lockReason", currentLockReason);
			AppendJson(builder, "lastAttemptAction", lastAttemptAction);
			AppendJson(builder, "lastAttemptOutcome", lastAttemptOutcome);
			AppendJson(builder, "lastAttemptReason", lastAttemptReason);
			AppendJson(builder, "lastAttemptTimestampUtc", lastAttemptTimestampUtc);
			AppendJson(builder, "lastOrderSummary", lastOrderSummary);
			AppendJson(builder, "lastExecutionSummary", lastExecutionSummary);
			AppendJson(builder, "dashboardOrderQuantity", dashboardOrderQuantity);
			AppendJson(builder, "lastCommandAcknowledgement", lastCommandAcknowledgement);
			AppendJson(builder, "lastAppliedCommandId", lastAppliedCommandId);
			AppendJson(builder, "lastAppliedCommandAction", lastAppliedCommandAction);
			AppendJson(builder, "realizedPnL", GetCurrentSessionPnL());
			AppendJson(builder, "unrealizedPnL", displayFlat ? 0 : GetUnrealizedPnL(currentPrice));
			AppendJson(builder, "accountBalance", GetAccountBalance());
			AppendJson(builder, "diagnosticOnly", false);
			AppendJson(builder, "statusTimestampUtc", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
			AppendJson(builder, "intentScore", analysis == null ? 0 : analysis.IntentScore, false);
			builder.Append("}");

			string payload = builder.ToString();
			try
			{
				if (!TryPublishDashboardStatus(payload))
					WriteAtomicText(GetDashboardStatusPath(), payload);
			}
			catch
			{
			}
		}

		private void WriteHeartbeat(BarData bar, SignalResult analysis)
		{
			if (!EnableDashboardControl)
				return;

			string heartbeat = string.Format(
				CultureInfo.InvariantCulture,
				"{0}|mode={1}|state={2}|bar={3}|price={4:0.00}|direction={5}|score={6:0.00}",
				DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
				GetEffectiveMode(),
				State,
				CurrentBar,
				bar == null ? 0 : bar.Close,
				analysis == null ? string.Empty : analysis.Direction.ToString(),
				analysis == null ? 0 : analysis.IntentScore);

			try
			{
				WriteAtomicText(GetDashboardHeartbeatPath(), heartbeat);
			}
			catch
			{
			}
		}

		private void WriteStartupDiagnostics(string phase)
		{
			if (!EnableDashboardControl)
				return;

			StringBuilder builder = new StringBuilder(256);
			builder.Append("{");
			AppendJson(builder, "connected", State != State.Terminated);
			AppendJson(builder, "mode", GetEffectiveMode().ToString());
			AppendJson(builder, "executionEnabled", dashboardExecutionEnabled);
			AppendJson(builder, "position", Position == null ? "Unknown" : Position.MarketPosition.ToString());
			AppendJson(builder, "lockReason", currentLockReason);
			AppendJson(builder, "diagnosticPhase", phase ?? string.Empty);
			AppendJson(builder, "barsInProgress", BarsInProgress);
			AppendJson(builder, "currentBar", CurrentBar);
			AppendJson(builder, "requiredBars", RequiredBars);
			AppendJson(builder, "useHigherTimeframeFilter", UseHigherTimeframeFilter);
			AppendJson(builder, "higherTimeframeMinutes", HigherTimeframeMinutes);
			AppendJson(builder, "diagnosticOnly", true);
			AppendJsonLast(builder, "statusTimestampUtc", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
			builder.Append("}");

			try
			{
				if (!TryPublishDashboardStatus(builder.ToString()))
					WriteAtomicText(GetDashboardStatusPath(), builder.ToString());
			}
			catch
			{
			}

			try
			{
				WriteAtomicText(
					GetDashboardHeartbeatPath(),
					DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) +
					"|phase=" + (phase ?? string.Empty) +
					"|state=" + State.ToString() +
					"|bip=" + BarsInProgress.ToString(CultureInfo.InvariantCulture) +
					"|bar=" + CurrentBar.ToString(CultureInfo.InvariantCulture));
			}
			catch
			{
			}
		}

		private void LogDiagnostic(string message)
		{
			try
			{
				Print("[IntentAutoTraderV01] " + message);
			}
			catch
			{
			}
		}

		private static void WriteAtomicText(string path, string content)
		{
			string tempPath = path + ".tmp";
			File.WriteAllText(tempPath, content ?? string.Empty, Encoding.UTF8);
			try
			{
				File.Delete(path);
			}
			catch
			{
			}

			File.Move(tempPath, path);
		}

		private bool TryPublishDashboardStatus(string payload)
		{
			// Non-blocking hand-off to the background bridge; the instrument thread never does HTTP.
			if (dashboardBridge == null || string.IsNullOrWhiteSpace(payload))
				return false;

			dashboardBridge.EnqueueStatus(payload);
			return true;
		}

		private void UpdateAttemptState(string action, string outcome, string reason)
		{
			lastAttemptAction = string.IsNullOrWhiteSpace(action) ? "None" : action;
			lastAttemptOutcome = string.IsNullOrWhiteSpace(outcome) ? "None" : outcome;
			lastAttemptReason = reason ?? string.Empty;
			lastAttemptTimestampUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
		}

		private static string GetRecommendedActionLabel(SignalResult analysis)
		{
			if (analysis == null)
				return "None";
			return analysis.RecommendedTradeAction.ToString();
		}

		private double GetUnrealizedPnL(double currentPrice)
		{
			try
			{
				return Position == null ? 0 : Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, currentPrice);
			}
			catch
			{
				return 0;
			}
		}

		private string GetTimeframeModeLabel()
		{
			string baseLabel = BarsPeriod == null
				? "Primary"
				: BarsPeriod.BarsPeriodType == BarsPeriodType.Minute
					? BarsPeriod.Value.ToString(CultureInfo.InvariantCulture) + "m"
					: BarsPeriod.BarsPeriodType + " " + BarsPeriod.Value.ToString(CultureInfo.InvariantCulture);

			if (!UseHigherTimeframeFilter || HigherTimeframeMinutes <= 0)
				return baseLabel;

			return baseLabel + "/" + HigherTimeframeMinutes.ToString(CultureInfo.InvariantCulture) + "m";
		}

		private MarketPosition GetDisplayPosition()
		{
			if (suppressHistoricalStrategyPosition)
				return MarketPosition.Flat;

			return Position == null ? MarketPosition.Flat : Position.MarketPosition;
		}

		private double GetAccountBalance()
		{
			try
			{
				return Account == null ? 0 : Convert.ToDouble(Account.Get(AccountItem.CashValue, Currency.UsDollar), CultureInfo.InvariantCulture);
			}
			catch
			{
				return 0;
			}
		}

		private static void AppendJson(StringBuilder builder, string name, string value)
		{
			builder.Append("\"").Append(name).Append("\":\"").Append(EscapeJson(value)).Append("\",");
		}

		private static void AppendJson(StringBuilder builder, string name, bool value)
		{
			builder.Append("\"").Append(name).Append("\":").Append(value ? "true" : "false").Append(",");
		}

		private static void AppendJson(StringBuilder builder, string name, int value)
		{
			builder.Append("\"").Append(name).Append("\":").Append(value.ToString(CultureInfo.InvariantCulture)).Append(",");
		}

		private static void AppendJson(StringBuilder builder, string name, double value, bool appendComma = true)
		{
			builder.Append("\"").Append(name).Append("\":").Append(value.ToString("0.######", CultureInfo.InvariantCulture));
			if (appendComma)
				builder.Append(",");
		}

		private static void AppendJsonLast(StringBuilder builder, string name, string value)
		{
			builder.Append("\"").Append(name).Append("\":\"").Append(EscapeJson(value)).Append("\"");
		}

		private static string EscapeJson(string value)
		{
			return string.IsNullOrEmpty(value) ? string.Empty : value.Replace("\\", "\\\\").Replace("\"", "\\\"");
		}

		private static int ParseIntOrDefault(string value, int fallback, int minValue)
		{
			int parsed;
			if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
				return fallback;
			return Math.Max(minValue, parsed);
		}

		private static double ParseDoubleOrDefault(string value, double fallback, double minValue)
		{
			double parsed;
			if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
				return fallback;
			return Math.Max(minValue, parsed);
		}

		private static long ParseCommandId(string command)
		{
			long commandId;
			return long.TryParse(ParseCommandValue(command, "id"), NumberStyles.Integer, CultureInfo.InvariantCulture, out commandId) ? commandId : 0;
		}

		private static string ParseCommandValue(string command, string key)
		{
			if (string.IsNullOrWhiteSpace(command) || string.IsNullOrWhiteSpace(key))
				return string.Empty;

			string[] parts = command.Split('&');
			for (int index = 0; index < parts.Length; index++)
			{
				string[] pair = parts[index].Split(new[] { '=' }, 2);
				if (pair.Length != 2)
					continue;
				if (!string.Equals(pair[0], key, StringComparison.OrdinalIgnoreCase))
					continue;
				return Uri.UnescapeDataString(pair[1]);
			}

			return string.Empty;
		}

		private static string GetDashboardControlPath()
		{
			return Path.Combine(Path.GetTempPath(), DashboardControlFileName);
		}

		private static string GetDashboardStatusPath()
		{
			return Path.Combine(Path.GetTempPath(), DashboardStatusFileName);
		}

		private static string GetDashboardHeartbeatPath()
		{
			return Path.Combine(Path.GetTempPath(), DashboardHeartbeatFileName);
		}

		private double BuildTargetPrice(bool isLongSignal, double entryPrice, double stopPrice)
		{
			if (!UseProfitTarget || entryPrice <= 0 || stopPrice <= 0)
				return 0;

			double risk = Math.Abs(entryPrice - stopPrice);
			if (risk < TickSize)
				return 0;

			double targetPrice = isLongSignal
				? entryPrice + (risk * RewardRiskMultiple)
				: entryPrice - (risk * RewardRiskMultiple);

			return Instrument != null && Instrument.MasterInstrument != null
				? Instrument.MasterInstrument.RoundToTickSize(targetPrice)
				: targetPrice;
		}

		private double ParsePrice(string value)
		{
			double parsed;
			return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) ? parsed : 0;
		}

		private EngineSettings BuildSettings()
		{
			return new EngineSettings
			{
				SignalThreshold = SignalThreshold,
				ImbalanceVolumeSpikeThreshold = ImbalanceVolumeSpikeThreshold,
				AbsorptionVolumeSpikeThreshold = AbsorptionVolumeSpikeThreshold,
				AbsorptionWickThreshold = AbsorptionWickThreshold,
				SweepVolumeSpikeThreshold = SweepVolumeSpikeThreshold,
				SweepWickThreshold = SweepWickThreshold,
				BreakoutExcursionTicks = BreakoutExcursionTicks,
				ReclaimTicks = ReclaimTicks,
				ImbalanceRatioThreshold = ImbalanceRatioThreshold,
				AbsorptionDeltaThresholdRatio = AbsorptionDeltaThresholdRatio,
				AbsorptionPriceEfficiencyThreshold = AbsorptionPriceEfficiencyThreshold,
				MinImbalanceVolumePerLevel = MinImbalanceVolumePerLevel,
				ImbalanceWeight = ImbalanceWeight,
				AbsorptionWeight = AbsorptionWeight,
				FailedBreakoutWeight = FailedBreakoutWeight,
				LiquiditySweepWeight = LiquiditySweepWeight,
				BreakoutContinuationWeight = BreakoutContinuationWeight,
				ConfluenceBonus = ConfluenceBonus,
				ExpansiveVolumeBonus = ExpansiveVolumeBonus,
				NeutralityBuffer = NeutralityBuffer,
				ImbalanceLevelNormalizationSpan = ImbalanceLevelNormalizationSpan,
				ImbalanceRatioNormalizationSpan = ImbalanceRatioNormalizationSpan,
				DeltaPerVolumeBaseline = DeltaPerVolumeBaseline,
				DeltaPerVolumeNormalizationSpan = DeltaPerVolumeNormalizationSpan,
				CloseLocationNormalizationSpan = CloseLocationNormalizationSpan,
				FallbackCloseLocationNormalizationSpan = FallbackCloseLocationNormalizationSpan,
				BodyRatioBaseline = BodyRatioBaseline,
				BodyRatioNormalizationSpan = BodyRatioNormalizationSpan,
				VolumeSpikeNormalizationSpan = VolumeSpikeNormalizationSpan,
				AbsorptionWickNormalizationSpan = AbsorptionWickNormalizationSpan,
				RangeExpansionPenaltyThreshold = RangeExpansionPenaltyThreshold,
				RangeExpansionNormalizationBaseline = RangeExpansionNormalizationBaseline,
				RangeExpansionNormalizationSpan = RangeExpansionNormalizationSpan,
				BreakoutNormalizationSpan = BreakoutNormalizationSpan,
				SweepWickNormalizationSpan = SweepWickNormalizationSpan,
				SweepVolumeNormalizationSpan = SweepVolumeNormalizationSpan,
				BreakoutZoneDeltaBaseline = BreakoutZoneDeltaBaseline,
				BreakoutZoneDeltaNormalizationSpan = BreakoutZoneDeltaNormalizationSpan,
				ExpansiveVolumeRangeExpansionThreshold = ExpansiveVolumeRangeExpansionThreshold,
				ContradictionPenaltyFloorMultiplier = ContradictionPenaltyFloorMultiplier,
				ContradictionSuppressionFactor = ContradictionSuppressionFactor,
				PriorSignalConfirmationBonus = PriorSignalConfirmationBonus,
				PriorSignalOppositionMultiplier = PriorSignalOppositionMultiplier,
				BullishTrendStructureThreshold = BullishTrendStructureThreshold,
				BearishTrendStructureThreshold = BearishTrendStructureThreshold,
				ReversalCloseLocationThreshold = ReversalCloseLocationThreshold,
				BreakoutCloseThroughLevelTicks = BreakoutCloseThroughLevelTicks,
				BreakoutVolumeSpikeThreshold = BreakoutVolumeSpikeThreshold,
				ContinuationTradeThreshold = ContinuationTradeThreshold,
				ReversalTradeThreshold = ReversalTradeThreshold,
				PullbackTradeThreshold = PullbackTradeThreshold
			};
		}

		private BarData BuildBarData(EngineSettings settings, int seriesIndex, IntentDirection priorDirection, double priorScore)
		{
			double high = Highs[seriesIndex][0];
			double low = Lows[seriesIndex][0];
			double tickSize = Math.Max(TickSize, 0.0000001);
			VolumetricBarsType volumetricBarsType = BarsArray[seriesIndex] != null ? BarsArray[seriesIndex].BarsType as VolumetricBarsType : null;
			int currentBarIndex = CurrentBars[seriesIndex];
			VolumetricData volumetricData = volumetricBarsType != null && volumetricBarsType.Volumes != null && currentBarIndex >= 0 && currentBarIndex < volumetricBarsType.Volumes.Length
				? volumetricBarsType.Volumes[currentBarIndex]
				: null;

			return new BarData
			{
				TimestampUtc = Times[seriesIndex][0].ToUniversalTime(),
				Open = Opens[seriesIndex][0],
				High = high,
				Low = low,
				Close = Closes[seriesIndex][0],
				Volume = (long)Volumes[seriesIndex][0],
				AverageVolume = AverageVolume(VolumeLookback, seriesIndex),
				AverageRange = AverageRange(RangeLookback, seriesIndex),
				PriorSwingHigh = PriorHigh(StructureLookback, seriesIndex),
				PriorSwingLow = PriorLow(StructureLookback, seriesIndex),
				PriorSignalDirection = priorDirection,
				PriorIntentScore = priorScore,
				TickSize = tickSize,
				OrderFlow = volumetricData != null
					? BuildOrderFlowData(volumetricData, low, high, tickSize, settings)
					: new OrderFlowData()
			};
		}

		private OrderFlowData BuildOrderFlowData(VolumetricData volumetricData, double low, double high, double tickSize, EngineSettings settings)
		{
			OrderFlowData orderFlow = new OrderFlowData
			{
				IsAvailable = true,
				TotalBuyingVolume = volumetricData.TotalBuyingVolume,
				TotalSellingVolume = volumetricData.TotalSellingVolume,
				BarDelta = volumetricData.BarDelta,
				DeltaSh = volumetricData.DeltaSh,
				DeltaSl = volumetricData.DeltaSl,
				PriceLevels = new System.Collections.Generic.List<OrderFlowPriceLevel>()
			};

			double maxAskRatio = 0;
			double maxBidRatio = 0;
			int levelCount = (int)Math.Round((high - low) / tickSize);

			for (int levelIndex = 0; levelIndex <= levelCount; levelIndex++)
			{
				double price = low + (levelIndex * tickSize);
				long askVolume = volumetricData.GetAskVolumeForPrice(price);
				long bidVolume = volumetricData.GetBidVolumeForPrice(price);
				orderFlow.PriceLevels.Add(new OrderFlowPriceLevel
				{
					Price = price,
					AskVolume = askVolume,
					BidVolume = bidVolume
				});

				if (askVolume >= settings.MinImbalanceVolumePerLevel)
				{
					double askRatio = SignalMath.SafeRatio(askVolume, Math.Max(1, bidVolume));
					maxAskRatio = Math.Max(maxAskRatio, askRatio);
					if (askRatio >= settings.ImbalanceRatioThreshold)
						orderFlow.AskImbalanceLevels++;
				}

				if (bidVolume >= settings.MinImbalanceVolumePerLevel)
				{
					double bidRatio = SignalMath.SafeRatio(bidVolume, Math.Max(1, askVolume));
					maxBidRatio = Math.Max(maxBidRatio, bidRatio);
					if (bidRatio >= settings.ImbalanceRatioThreshold)
						orderFlow.BidImbalanceLevels++;
				}
			}

			orderFlow.AskImbalanceRatio = maxAskRatio;
			orderFlow.BidImbalanceRatio = maxBidRatio;
			orderFlow.DeltaPerVolume = SignalMath.SafeRatio(Math.Abs(orderFlow.BarDelta), Math.Max(1, volumetricData.TotalVolume));
			return orderFlow;
		}

		private double AverageVolume(int lookback, int seriesIndex)
		{
			double sum = 0;
			int bars = Math.Min(CurrentBars[seriesIndex], Math.Max(1, lookback));

			for (int barsAgo = 1; barsAgo <= bars; barsAgo++)
				sum += Volumes[seriesIndex][barsAgo];

			return sum / Math.Max(1, bars);
		}

		private double AverageRange(int lookback, int seriesIndex)
		{
			return AverageRange(lookback, 0, seriesIndex);
		}

		private double AverageRange(int lookback, int barsAgoOffset, int seriesIndex)
		{
			double sum = 0;
			int startBarsAgo = Math.Max(1, barsAgoOffset + 1);
			int availableBars = Math.Max(0, CurrentBars[seriesIndex] - startBarsAgo);
			int bars = Math.Min(availableBars, Math.Max(1, lookback));

			for (int barsAgo = 0; barsAgo < bars; barsAgo++)
				sum += Math.Max(Highs[seriesIndex][barsAgo + startBarsAgo] - Lows[seriesIndex][barsAgo + startBarsAgo], TickSize);

			return sum / Math.Max(1, bars);
		}

		private double PriorHigh(int lookback, int seriesIndex)
		{
			double highest = double.MinValue;
			int bars = Math.Min(CurrentBars[seriesIndex], Math.Max(1, lookback));

			for (int barsAgo = 1; barsAgo <= bars; barsAgo++)
				highest = Math.Max(highest, Highs[seriesIndex][barsAgo]);

			return highest == double.MinValue ? Highs[seriesIndex][0] : highest;
		}

		private double PriorLow(int lookback, int seriesIndex)
		{
			double lowest = double.MaxValue;
			int bars = Math.Min(CurrentBars[seriesIndex], Math.Max(1, lookback));

			for (int barsAgo = 1; barsAgo <= bars; barsAgo++)
				lowest = Math.Min(lowest, Lows[seriesIndex][barsAgo]);

			return lowest == double.MaxValue ? Lows[seriesIndex][0] : lowest;
		}

		#region Strategy controls
		[NinjaScriptProperty]
		[Display(Name = "ExecutionMode", GroupName = "Trade Controls", Order = 0)]
		public IntentExecutionMode ExecutionMode { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "AllowDashboardManualCommandsOutsideRealtime", GroupName = "Trade Controls", Order = 1)]
		public bool AllowDashboardManualCommandsOutsideRealtime { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "AllowLongs", GroupName = "Trade Controls", Order = 2)]
		public bool AllowLongs { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "AllowShorts", GroupName = "Trade Controls", Order = 3)]
		public bool AllowShorts { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "AllowReversals", GroupName = "Trade Controls", Order = 4)]
		public bool AllowReversals { get; set; }

		[Range(1, int.MaxValue)]
		[NinjaScriptProperty]
		[Display(Name = "Quantity", GroupName = "Trade Controls", Order = 5)]
		public int Quantity { get; set; }

		[Range(1, int.MaxValue)]
		[NinjaScriptProperty]
		[Display(Name = "MaxContracts", GroupName = "Risk", Order = 12)]
		public int MaxContracts { get; set; }

		[Range(1, int.MaxValue)]
		[NinjaScriptProperty]
		[Display(Name = "DashboardOrderQuantity", GroupName = "Trade Controls", Order = 6)]
		public int DashboardOrderQuantity
		{
			get { return dashboardOrderQuantity; }
			set { dashboardOrderQuantity = Math.Max(1, value); }
		}

		[Range(0, 65535)]
		[NinjaScriptProperty]
		[Display(Name = "DashboardBridgePort", GroupName = "Trade Controls", Order = 7)]
		public int DashboardBridgePort { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "UseEngineStop", GroupName = "Risk", Order = 8)]
		public bool UseEngineStop { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "UseProfitTarget", GroupName = "Risk", Order = 9)]
		public bool UseProfitTarget { get; set; }

		[Range(0.25, 10.0)]
		[NinjaScriptProperty]
		[Display(Name = "RewardRiskMultiple", GroupName = "Risk", Order = 10)]
		public double RewardRiskMultiple { get; set; }

		[Range(1, 200)]
		[NinjaScriptProperty]
		[Display(Name = "MinimumStopDistanceTicks", GroupName = "Risk", Order = 11)]
		public int MinimumStopDistanceTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "PrintManualSignals", GroupName = "Trade Controls", Order = 9)]
		public bool PrintManualSignals { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "DrawManualArrows", GroupName = "Trade Controls", Order = 10)]
		public bool DrawManualArrows { get; set; }

		[Range(1, 20)]
		[NinjaScriptProperty]
		[Display(Name = "ManualArrowOffsetTicks", GroupName = "Trade Controls", Order = 11)]
		public int ManualArrowOffsetTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "EnableDashboardControl", GroupName = "Trade Controls", Order = 12)]
		public bool EnableDashboardControl { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "UseHigherTimeframeFilter", GroupName = "Trade Controls", Order = 13)]
		public bool UseHigherTimeframeFilter { get; set; }

		[Range(5, 240)]
		[NinjaScriptProperty]
		[Display(Name = "HigherTimeframeMinutes", GroupName = "Trade Controls", Order = 14)]
		public int HigherTimeframeMinutes { get; set; }

		[Range(1.0, 100.0)]
		[NinjaScriptProperty]
		[Display(Name = "MinHigherTimeframeIntentScore", GroupName = "Trade Rules", Order = 15)]
		public double MinHigherTimeframeIntentScore { get; set; }

		[Range(1, 5)]
		[NinjaScriptProperty]
		[Display(Name = "RegimeFlipOppositionBars", GroupName = "Trade Rules", Order = 16)]
		public int RegimeFlipOppositionBars { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "ShowCurrentPriceLine", GroupName = "Visuals", Order = 17)]
		public bool ShowCurrentPriceLine { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "ShowTradeLevels", GroupName = "Visuals", Order = 18)]
		public bool ShowTradeLevels { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "ShowSessionLevels", GroupName = "Visuals", Order = 19)]
		public bool ShowSessionLevels { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "ShowVisualSummary", GroupName = "Visuals", Order = 20)]
		public bool ShowVisualSummary { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "EnableChopFilter", GroupName = "Trade Controls", Order = 21)]
		public bool EnableChopFilter { get; set; }

		[Range(0.1, 2.0)]
		[NinjaScriptProperty]
		[Display(Name = "CompressionRangeExpansionMax", GroupName = "Trade Rules", Order = 22)]
		public double CompressionRangeExpansionMax { get; set; }

		[Range(0.5, 5.0)]
		[NinjaScriptProperty]
		[Display(Name = "ExpansionRangeExpansionMin", GroupName = "Trade Rules", Order = 23)]
		public double ExpansionRangeExpansionMin { get; set; }

		[Range(0.5, 5.0)]
		[NinjaScriptProperty]
		[Display(Name = "ExpansionVolumeSpikeMin", GroupName = "Trade Rules", Order = 24)]
		public double ExpansionVolumeSpikeMin { get; set; }

		[Range(1.0, 100.0)]
		[NinjaScriptProperty]
		[Display(Name = "MinAutoIntentScore", GroupName = "Trade Rules", Order = 25)]
		public double MinAutoIntentScore { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "TradeContinuationOnly", GroupName = "Trade Rules", Order = 26)]
		public bool TradeContinuationOnly { get; set; }

		[Range(0, 100)]
		[NinjaScriptProperty]
		[Display(Name = "MaxTradesPerSession", GroupName = "Trade Rules", Order = 27)]
		public int MaxTradesPerSession { get; set; }
		#endregion

		#region Engine parameters
		[Range(5, 200)]
		[NinjaScriptProperty]
		[Display(Name = "VolumeLookback", GroupName = "Parameters", Order = 20)]
		public int VolumeLookback { get; set; }

		[Range(5, 200)]
		[NinjaScriptProperty]
		[Display(Name = "RangeLookback", GroupName = "Parameters", Order = 21)]
		public int RangeLookback { get; set; }

		[Range(5, 200)]
		[NinjaScriptProperty]
		[Display(Name = "StructureLookback", GroupName = "Parameters", Order = 22)]
		public int StructureLookback { get; set; }

		[Range(1, 100)]
		[NinjaScriptProperty]
		[Display(Name = "SignalThreshold", GroupName = "Parameters", Order = 23)]
		public int SignalThreshold { get; set; }

		[Range(0.5, 5.0)]
		[NinjaScriptProperty]
		[Display(Name = "ImbalanceVolumeSpikeThreshold", GroupName = "Thresholds", Order = 24)]
		public double ImbalanceVolumeSpikeThreshold { get; set; }

		[Range(0.5, 5.0)]
		[NinjaScriptProperty]
		[Display(Name = "AbsorptionVolumeSpikeThreshold", GroupName = "Thresholds", Order = 25)]
		public double AbsorptionVolumeSpikeThreshold { get; set; }

		[Range(0.05, 0.95)]
		[NinjaScriptProperty]
		[Display(Name = "AbsorptionWickThreshold", GroupName = "Thresholds", Order = 26)]
		public double AbsorptionWickThreshold { get; set; }

		[Range(0.5, 5.0)]
		[NinjaScriptProperty]
		[Display(Name = "SweepVolumeSpikeThreshold", GroupName = "Thresholds", Order = 27)]
		public double SweepVolumeSpikeThreshold { get; set; }

		[Range(0.05, 0.95)]
		[NinjaScriptProperty]
		[Display(Name = "SweepWickThreshold", GroupName = "Thresholds", Order = 28)]
		public double SweepWickThreshold { get; set; }

		[Range(1, 20)]
		[NinjaScriptProperty]
		[Display(Name = "BreakoutExcursionTicks", GroupName = "Thresholds", Order = 29)]
		public int BreakoutExcursionTicks { get; set; }

		[Range(1, 20)]
		[NinjaScriptProperty]
		[Display(Name = "ReclaimTicks", GroupName = "Thresholds", Order = 30)]
		public int ReclaimTicks { get; set; }

		[Range(1.1, 10.0)]
		[NinjaScriptProperty]
		[Display(Name = "ImbalanceRatioThreshold", GroupName = "OrderFlow", Order = 31)]
		public double ImbalanceRatioThreshold { get; set; }

		[Range(0.01, 1.00)]
		[NinjaScriptProperty]
		[Display(Name = "AbsorptionDeltaThresholdRatio", GroupName = "OrderFlow", Order = 32)]
		public double AbsorptionDeltaThresholdRatio { get; set; }

		[Range(0.05, 1.00)]
		[NinjaScriptProperty]
		[Display(Name = "AbsorptionPriceEfficiencyThreshold", GroupName = "OrderFlow", Order = 33)]
		public double AbsorptionPriceEfficiencyThreshold { get; set; }

		[Range(1, 1000)]
		[NinjaScriptProperty]
		[Display(Name = "MinImbalanceVolumePerLevel", GroupName = "OrderFlow", Order = 34)]
		public long MinImbalanceVolumePerLevel { get; set; }

		[Range(0.0, 1.0)]
		[NinjaScriptProperty]
		[Display(Name = "ImbalanceWeight", GroupName = "Scoring", Order = 35)]
		public double ImbalanceWeight { get; set; }

		[Range(0.0, 1.0)]
		[NinjaScriptProperty]
		[Display(Name = "AbsorptionWeight", GroupName = "Scoring", Order = 36)]
		public double AbsorptionWeight { get; set; }

		[Range(0.0, 1.0)]
		[NinjaScriptProperty]
		[Display(Name = "FailedBreakoutWeight", GroupName = "Scoring", Order = 37)]
		public double FailedBreakoutWeight { get; set; }

		[Range(0.0, 1.0)]
		[NinjaScriptProperty]
		[Display(Name = "LiquiditySweepWeight", GroupName = "Scoring", Order = 38)]
		public double LiquiditySweepWeight { get; set; }

		[Range(0.0, 1.0)]
		[NinjaScriptProperty]
		[Display(Name = "BreakoutContinuationWeight", GroupName = "Scoring", Order = 39)]
		public double BreakoutContinuationWeight { get; set; }

		[Range(0.0, 25.0)]
		[NinjaScriptProperty]
		[Display(Name = "ConfluenceBonus", GroupName = "Scoring", Order = 40)]
		public double ConfluenceBonus { get; set; }

		[Range(0.0, 25.0)]
		[NinjaScriptProperty]
		[Display(Name = "ExpansiveVolumeBonus", GroupName = "Scoring", Order = 41)]
		public double ExpansiveVolumeBonus { get; set; }

		[Range(0.0, 20.0)]
		[NinjaScriptProperty]
		[Display(Name = "NeutralityBuffer", GroupName = "Scoring", Order = 42)]
		public double NeutralityBuffer { get; set; }

		[Range(0.1, 20.0)]
		[NinjaScriptProperty]
		[Display(Name = "ImbalanceLevelNormalizationSpan", GroupName = "Normalization", Order = 43)]
		public double ImbalanceLevelNormalizationSpan { get; set; }

		[Range(0.1, 20.0)]
		[NinjaScriptProperty]
		[Display(Name = "ImbalanceRatioNormalizationSpan", GroupName = "Normalization", Order = 44)]
		public double ImbalanceRatioNormalizationSpan { get; set; }

		[Range(0.0, 2.0)]
		[NinjaScriptProperty]
		[Display(Name = "DeltaPerVolumeBaseline", GroupName = "Normalization", Order = 45)]
		public double DeltaPerVolumeBaseline { get; set; }

		[Range(0.1, 5.0)]
		[NinjaScriptProperty]
		[Display(Name = "DeltaPerVolumeNormalizationSpan", GroupName = "Normalization", Order = 46)]
		public double DeltaPerVolumeNormalizationSpan { get; set; }

		[Range(0.1, 1.0)]
		[NinjaScriptProperty]
		[Display(Name = "CloseLocationNormalizationSpan", GroupName = "Normalization", Order = 47)]
		public double CloseLocationNormalizationSpan { get; set; }

		[Range(0.1, 1.0)]
		[NinjaScriptProperty]
		[Display(Name = "FallbackCloseLocationNormalizationSpan", GroupName = "Normalization", Order = 48)]
		public double FallbackCloseLocationNormalizationSpan { get; set; }

		[Range(0.0, 1.0)]
		[NinjaScriptProperty]
		[Display(Name = "BodyRatioBaseline", GroupName = "Normalization", Order = 49)]
		public double BodyRatioBaseline { get; set; }

		[Range(0.1, 2.0)]
		[NinjaScriptProperty]
		[Display(Name = "BodyRatioNormalizationSpan", GroupName = "Normalization", Order = 50)]
		public double BodyRatioNormalizationSpan { get; set; }

		[Range(0.1, 5.0)]
		[NinjaScriptProperty]
		[Display(Name = "VolumeSpikeNormalizationSpan", GroupName = "Normalization", Order = 51)]
		public double VolumeSpikeNormalizationSpan { get; set; }

		[Range(0.1, 2.0)]
		[NinjaScriptProperty]
		[Display(Name = "AbsorptionWickNormalizationSpan", GroupName = "Normalization", Order = 52)]
		public double AbsorptionWickNormalizationSpan { get; set; }

		[Range(0.1, 5.0)]
		[NinjaScriptProperty]
		[Display(Name = "RangeExpansionPenaltyThreshold", GroupName = "Normalization", Order = 53)]
		public double RangeExpansionPenaltyThreshold { get; set; }

		[Range(0.1, 5.0)]
		[NinjaScriptProperty]
		[Display(Name = "RangeExpansionNormalizationBaseline", GroupName = "Normalization", Order = 54)]
		public double RangeExpansionNormalizationBaseline { get; set; }

		[Range(0.1, 5.0)]
		[NinjaScriptProperty]
		[Display(Name = "RangeExpansionNormalizationSpan", GroupName = "Normalization", Order = 55)]
		public double RangeExpansionNormalizationSpan { get; set; }

		[Range(0.1, 20.0)]
		[NinjaScriptProperty]
		[Display(Name = "BreakoutNormalizationSpan", GroupName = "Normalization", Order = 56)]
		public double BreakoutNormalizationSpan { get; set; }

		[Range(0.1, 2.0)]
		[NinjaScriptProperty]
		[Display(Name = "SweepWickNormalizationSpan", GroupName = "Normalization", Order = 57)]
		public double SweepWickNormalizationSpan { get; set; }

		[Range(0.1, 5.0)]
		[NinjaScriptProperty]
		[Display(Name = "SweepVolumeNormalizationSpan", GroupName = "Normalization", Order = 58)]
		public double SweepVolumeNormalizationSpan { get; set; }

		[Range(0.0, 1.0)]
		[NinjaScriptProperty]
		[Display(Name = "BreakoutZoneDeltaBaseline", GroupName = "Normalization", Order = 59)]
		public double BreakoutZoneDeltaBaseline { get; set; }

		[Range(0.1, 5.0)]
		[NinjaScriptProperty]
		[Display(Name = "BreakoutZoneDeltaNormalizationSpan", GroupName = "Normalization", Order = 60)]
		public double BreakoutZoneDeltaNormalizationSpan { get; set; }

		[Range(0.1, 5.0)]
		[NinjaScriptProperty]
		[Display(Name = "ExpansiveVolumeRangeExpansionThreshold", GroupName = "Normalization", Order = 61)]
		public double ExpansiveVolumeRangeExpansionThreshold { get; set; }

		[Range(0.0, 1.0)]
		[NinjaScriptProperty]
		[Display(Name = "ContradictionPenaltyFloorMultiplier", GroupName = "Context", Order = 62)]
		public double ContradictionPenaltyFloorMultiplier { get; set; }

		[Range(0.0, 1.0)]
		[NinjaScriptProperty]
		[Display(Name = "ContradictionSuppressionFactor", GroupName = "Context", Order = 63)]
		public double ContradictionSuppressionFactor { get; set; }

		[Range(0.0, 20.0)]
		[NinjaScriptProperty]
		[Display(Name = "PriorSignalConfirmationBonus", GroupName = "Context", Order = 64)]
		public double PriorSignalConfirmationBonus { get; set; }

		[Range(0.0, 1.0)]
		[NinjaScriptProperty]
		[Display(Name = "PriorSignalOppositionMultiplier", GroupName = "Context", Order = 65)]
		public double PriorSignalOppositionMultiplier { get; set; }

		[Range(0.0, 1.0)]
		[NinjaScriptProperty]
		[Display(Name = "BullishTrendStructureThreshold", GroupName = "Context", Order = 66)]
		public double BullishTrendStructureThreshold { get; set; }

		[Range(0.0, 1.0)]
		[NinjaScriptProperty]
		[Display(Name = "BearishTrendStructureThreshold", GroupName = "Context", Order = 67)]
		public double BearishTrendStructureThreshold { get; set; }

		[Range(0.0, 1.0)]
		[NinjaScriptProperty]
		[Display(Name = "ReversalCloseLocationThreshold", GroupName = "Context", Order = 68)]
		public double ReversalCloseLocationThreshold { get; set; }

		[Range(0.0, 10.0)]
		[NinjaScriptProperty]
		[Display(Name = "BreakoutCloseThroughLevelTicks", GroupName = "Context", Order = 69)]
		public double BreakoutCloseThroughLevelTicks { get; set; }

		[Range(0.1, 5.0)]
		[NinjaScriptProperty]
		[Display(Name = "BreakoutVolumeSpikeThreshold", GroupName = "Context", Order = 70)]
		public double BreakoutVolumeSpikeThreshold { get; set; }

		[Range(1.0, 100.0)]
		[NinjaScriptProperty]
		[Display(Name = "ContinuationTradeThreshold", GroupName = "Trade Rules", Order = 71)]
		public double ContinuationTradeThreshold { get; set; }

		[Range(1.0, 100.0)]
		[NinjaScriptProperty]
		[Display(Name = "ReversalTradeThreshold", GroupName = "Trade Rules", Order = 72)]
		public double ReversalTradeThreshold { get; set; }

		[Range(1.0, 100.0)]
		[NinjaScriptProperty]
		[Display(Name = "PullbackTradeThreshold", GroupName = "Trade Rules", Order = 73)]
		public double PullbackTradeThreshold { get; set; }

		[Range(0, 100)]
		[NinjaScriptProperty]
		[Display(Name = "CooldownBars", GroupName = "Trade Rules", Order = 74)]
		public int CooldownBars { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "UseDailyLossLimit", GroupName = "Risk", Order = 75)]
		public bool UseDailyLossLimit { get; set; }

		[Range(1, 100000)]
		[NinjaScriptProperty]
		[Display(Name = "MaxDailyLossCurrency", GroupName = "Risk", Order = 76)]
		public double MaxDailyLossCurrency { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "UseFlatBeforeClose", GroupName = "Risk", Order = 77)]
		public bool UseFlatBeforeClose { get; set; }

		[Range(0, 235959)]
		[NinjaScriptProperty]
		[Display(Name = "FlatTime", GroupName = "Risk", Order = 78)]
		public int FlatTime { get; set; }
		#endregion
	}
}
