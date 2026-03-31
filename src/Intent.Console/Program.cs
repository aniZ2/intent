using System;
using Intent.Engine.Runtime;

namespace Intent.StreamRunner
{
	internal static class Program
	{
		private static int Main(string[] args)
		{
			RunnerOptions options = RunnerOptions.Parse(args);
			using (RunnerLogger logger = new RunnerLogger(options.LogFilePath))
			using (DecisionPacketSink packetSink = new DecisionPacketSink(options.PacketOutputPath))
			using (RawTickArchive tickArchive = new RawTickArchive(options.TickArchiveRootPath))
			using (DashboardBroadcaster dashboard = new DashboardBroadcaster(options.DashboardPort, logger))
			{
				IntentRuntime runtime = RuntimeFactory.Create(options);
				TcpTickServer server = new TcpTickServer(options, runtime, new TickJsonDeserializer(), logger, packetSink, tickArchive, dashboard);

				Console.CancelKeyPress += delegate(object sender, ConsoleCancelEventArgs eventArgs)
				{
					eventArgs.Cancel = true;
					server.Stop();
				};

				try
				{
					dashboard.Start();
					server.Run();
					return 0;
				}
				catch (Exception ex)
				{
					logger.Error("[fatal] " + ex.Message);
					return 1;
				}
			}
		}
	}
}
