using System;

namespace Intent.Engine.Signals
{
	public static class SignalMath
	{
		public static double Clamp01(double value)
		{
			if (value < 0)
				return 0;
			if (value > 1)
				return 1;
			return value;
		}

		public static double Clamp100(double value)
		{
			if (value < 0)
				return 0;
			if (value > 100)
				return 100;
			return value;
		}

		public static double SafeRatio(double numerator, double denominator)
		{
			if (Math.Abs(denominator) < 0.0000001)
				return 0;
			return numerator / denominator;
		}
	}
}
