using System;

namespace Intent.Replay
{
	internal static class Program
	{
		private static int Main(string[] args)
		{
			try
			{
				ReplayOptions options = ReplayOptions.Parse(args);
				new TickReplayClient(options).Run();
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
