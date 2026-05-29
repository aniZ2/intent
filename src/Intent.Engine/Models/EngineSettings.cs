namespace Intent.Engine.Models
{
	public sealed class EngineSettings
	{
		public EngineSettings()
		{
			SignalThreshold = 30;
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
			ContinuationTradeThreshold = 60.0;
			ReversalTradeThreshold = 65.0;
			PullbackTradeThreshold = 70.0;
		}

		public int SignalThreshold { get; set; }
		public double ImbalanceVolumeSpikeThreshold { get; set; }
		public double AbsorptionVolumeSpikeThreshold { get; set; }
		public double AbsorptionWickThreshold { get; set; }
		public double SweepVolumeSpikeThreshold { get; set; }
		public double SweepWickThreshold { get; set; }
		public int BreakoutExcursionTicks { get; set; }
		public int ReclaimTicks { get; set; }
		public double ImbalanceRatioThreshold { get; set; }
		public double AbsorptionDeltaThresholdRatio { get; set; }
		public double AbsorptionPriceEfficiencyThreshold { get; set; }
		public long MinImbalanceVolumePerLevel { get; set; }
		public double ImbalanceWeight { get; set; }
		public double AbsorptionWeight { get; set; }
		public double FailedBreakoutWeight { get; set; }
		public double LiquiditySweepWeight { get; set; }
		public double BreakoutContinuationWeight { get; set; }
		public double ConfluenceBonus { get; set; }
		public double ExpansiveVolumeBonus { get; set; }
		public double NeutralityBuffer { get; set; }
		public double ImbalanceLevelNormalizationSpan { get; set; }
		public double ImbalanceRatioNormalizationSpan { get; set; }
		public double DeltaPerVolumeBaseline { get; set; }
		public double DeltaPerVolumeNormalizationSpan { get; set; }
		public double CloseLocationNormalizationSpan { get; set; }
		public double FallbackCloseLocationNormalizationSpan { get; set; }
		public double BodyRatioBaseline { get; set; }
		public double BodyRatioNormalizationSpan { get; set; }
		public double VolumeSpikeNormalizationSpan { get; set; }
		public double AbsorptionWickNormalizationSpan { get; set; }
		public double RangeExpansionPenaltyThreshold { get; set; }
		public double RangeExpansionNormalizationBaseline { get; set; }
		public double RangeExpansionNormalizationSpan { get; set; }
		public double BreakoutNormalizationSpan { get; set; }
		public double SweepWickNormalizationSpan { get; set; }
		public double SweepVolumeNormalizationSpan { get; set; }
		public double BreakoutZoneDeltaBaseline { get; set; }
		public double BreakoutZoneDeltaNormalizationSpan { get; set; }
		public double ExpansiveVolumeRangeExpansionThreshold { get; set; }
		public double ContradictionPenaltyFloorMultiplier { get; set; }
		public double ContradictionSuppressionFactor { get; set; }
		public double PriorSignalConfirmationBonus { get; set; }
		public double PriorSignalOppositionMultiplier { get; set; }
		public double BullishTrendStructureThreshold { get; set; }
		public double BearishTrendStructureThreshold { get; set; }
		public double ReversalCloseLocationThreshold { get; set; }
		public double BreakoutCloseThroughLevelTicks { get; set; }
		public double BreakoutVolumeSpikeThreshold { get; set; }
		public double ContinuationTradeThreshold { get; set; }
		public double ReversalTradeThreshold { get; set; }
		public double PullbackTradeThreshold { get; set; }
	}
}
