using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Intent.Engine.Models;
using Intent.Engine.Runtime;

namespace Intent.StreamRunner
{
	internal sealed class TcpTickServer
	{
		private readonly RunnerOptions options;
		private readonly Func<IntentRuntime> runtimeFactory;
		private readonly Dictionary<string, IntentRuntime> runtimes;
		private readonly TickJsonDeserializer deserializer;
		private readonly RunnerLogger logger;
		private readonly DecisionPacketSink packetSink;
		private readonly RawTickArchive tickArchive;
		private readonly DashboardBroadcaster dashboard;
		private readonly TcpListener listener;
		private volatile bool stopRequested;
		private readonly DateTime startedAtUtc;
		private long totalTicksReceived;
		private long malformedTickCount;
		private long totalPacketsEmitted;

		public TcpTickServer(RunnerOptions options, Func<IntentRuntime> runtimeFactory, TickJsonDeserializer deserializer, RunnerLogger logger, DecisionPacketSink packetSink, RawTickArchive tickArchive, DashboardBroadcaster dashboard)
		{
			this.options = options;
			this.runtimeFactory = runtimeFactory;
			this.runtimes = new Dictionary<string, IntentRuntime>(StringComparer.Ordinal);
			this.deserializer = deserializer;
			this.logger = logger;
			this.packetSink = packetSink;
			this.tickArchive = tickArchive;
			this.dashboard = dashboard;
			startedAtUtc = DateTime.UtcNow;
			listener = new TcpListener(IPAddress.Parse(options.Host), options.Port);
		}

		public void Run()
		{
			listener.Start();
			logger.Info("[listener] " + options.Host + ":" + options.Port + " mode=" + DescribeMode());
			if (packetSink != null && packetSink.IsEnabled)
				logger.Info("[packets] writing decision packets to " + Path.GetFullPath(options.PacketOutputPath));
			if (tickArchive != null && tickArchive.IsEnabled)
				logger.Info("[ticks] archiving inbound ticks under " + tickArchive.RootDirectory);

			while (!stopRequested)
			{
				TcpClient client = null;
				try
				{
					client = listener.AcceptTcpClient();
					HandleClient(client);
				}
				catch (SocketException ex)
				{
					if (stopRequested)
						break;

					logger.Error("[socket] " + ex.Message);
				}
				finally
				{
					if (client != null)
						client.Dispose();
				}
			}

			foreach (IntentRuntime runtime in runtimes.Values)
				WriteEmissions(runtime.FlushPending());
			TimeSpan elapsed = DateTime.UtcNow - startedAtUtc;
			double elapsedSeconds = Math.Max(1.0, elapsed.TotalSeconds);
			logger.Info(string.Format("[summary] ticks={0} malformed={1} packets={2} ticksPerSec={3:0.##} packetsPerSec={4:0.##}", totalTicksReceived, malformedTickCount, totalPacketsEmitted, totalTicksReceived / elapsedSeconds, totalPacketsEmitted / elapsedSeconds));
		}

		public void Stop()
		{
			stopRequested = true;
			try
			{
				listener.Stop();
			}
			catch (SocketException)
			{
			}
		}

		private void HandleClient(TcpClient client)
		{
			logger.Info("[client] connected " + client.Client.RemoteEndPoint);
			client.NoDelay = true;

			try
			{
				using (NetworkStream stream = client.GetStream())
				using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true, 4096, false))
				{
					stream.ReadTimeout = 1000;
					while (!stopRequested)
					{
						string line;
						try
						{
							line = reader.ReadLine();
						}
						catch (IOException ex)
						{
							if (stopRequested)
								break;
							if (IsReadTimeout(ex))
								continue;
							throw;
						}

						if (stopRequested)
							break;
						if (line == null)
							break;
						if (string.IsNullOrWhiteSpace(line))
							continue;

						TickData tick;
						string error;
						if (!deserializer.TryDeserialize(line, out tick, out error))
						{
							malformedTickCount++;
							logger.Error("[malformed] " + error);
							continue;
						}

						if (tickArchive != null)
							tickArchive.WriteTick(tick.Instrument, tick.TimestampUtc, line);

						totalTicksReceived++;
						TickProcessingResult result = GetRuntime(tick.Instrument).OnTick(tick);
						WriteEmissions(result);
					}
				}
			}
			catch (IOException ex)
			{
				if (!stopRequested)
					logger.Error("[disconnect] " + ex.Message);
			}
			catch (SocketException ex)
			{
				if (!stopRequested)
					logger.Error("[disconnect] " + ex.Message);
			}
			finally
			{
				logger.Info("[client] disconnected");
			}
		}

		private IntentRuntime GetRuntime(string instrument)
		{
			string key = string.IsNullOrWhiteSpace(instrument)
				? (string.IsNullOrWhiteSpace(options.DefaultInstrument) ? string.Empty : options.DefaultInstrument)
				: instrument;
			IntentRuntime runtime;
			if (!runtimes.TryGetValue(key, out runtime))
			{
				runtime = runtimeFactory();
				runtimes[key] = runtime;
			}
			return runtime;
		}

		private void WriteEmissions(TickProcessingResult result)
		{
			if (result == null)
				return;

			for (int index = 0; index < result.Emissions.Count; index++)
			{
				string json = result.Emissions[index].Packet.ToJson();
				Console.WriteLine(json);
				if (packetSink != null)
					packetSink.WriteLine(json);
				if (dashboard != null)
					dashboard.Broadcast(json);
				totalPacketsEmitted++;
			}
		}

		private string DescribeMode()
		{
			if (options.EmitCompletedBars && options.EmitSignalEvents)
				return "hybrid";
			if (options.EmitCompletedBars)
				return "bars";
			return "signals";
		}

		private static bool IsReadTimeout(IOException ex)
		{
			SocketException socketException = ex.InnerException as SocketException;
			return socketException != null && socketException.SocketErrorCode == SocketError.TimedOut;
		}
	}
}
