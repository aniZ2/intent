using System;

namespace Intent.Sweep
{
	internal static class Program
	{
		private static int Main(string[] args)
		{
			try
			{
				if (args != null && Array.IndexOf(args, "--selftest") >= 0)
					return SelfTest.Run();

				SweepOptions options = SweepOptions.Parse(args);
				new ParameterSweepRunner(options).Run();
				return 0;
			}
			catch (Exception ex)
			{
				Console.Error.WriteLine("[fatal] " + ex.Message);
				return 1;
			}
		}
	}
}
