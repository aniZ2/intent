#region Using declarations
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Intent.Engine.Models;
using Intent.Engine.Transport;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
	internal interface ITickStreamPublisher : IDisposable
	{
		void Publish(TickData tick);
	}

	// Sends ticks over TCP from a dedicated background thread so NinjaTrader's
	// market-data callback never blocks on connect/write/flush. Publish() only
	// enqueues into a bounded queue and drops (with a throttled log) on overflow.
	internal sealed class TcpTickStreamPublisher : ITickStreamPublisher
	{
		private const int MaxQueueDepth = 4096;
		private const int ConnectTimeoutMs = 2000;

		private readonly string host;
		private readonly int port;
		private readonly Action<string> log;
		private readonly BlockingCollection<TickData> queue;
		private readonly Thread worker;
		private volatile bool stopping;
		private TcpClient client;
		private StreamWriter writer;
		private DateTime lastReconnectAttemptUtc;
		private long droppedCount;

		public TcpTickStreamPublisher(string host, int port, Action<string> log)
		{
			this.host = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host;
			this.port = port <= 0 ? 4100 : port;
			this.log = log ?? delegate { };
			this.queue = new BlockingCollection<TickData>(new ConcurrentQueue<TickData>(), MaxQueueDepth);
			this.worker = new Thread(RunSendLoop);
			this.worker.IsBackground = true;
			this.worker.Name = "IntentTickPublisher";
			this.worker.Start();
		}

		public void Publish(TickData tick)
		{
			if (tick == null || stopping)
				return;

			if (!queue.TryAdd(tick))
			{
				long dropped = Interlocked.Increment(ref droppedCount);
				if (dropped % 100 == 1)
					log(string.Format("[Intent.Stream] send queue full; dropped {0} tick(s)", dropped));
			}
		}

		public void Dispose()
		{
			stopping = true;
			try
			{
				queue.CompleteAdding();
			}
			catch
			{
			}

			try
			{
				if (worker != null && worker.IsAlive)
					worker.Join(1000);
			}
			catch
			{
			}

			ResetConnection();
		}

		private void RunSendLoop()
		{
			try
			{
				foreach (TickData tick in queue.GetConsumingEnumerable())
				{
					if (stopping)
						break;
					TrySend(tick);
				}
			}
			catch (ObjectDisposedException)
			{
			}
			catch (InvalidOperationException)
			{
			}
		}

		private void TrySend(TickData tick)
		{
			if (!EnsureConnected())
				return;

			try
			{
				writer.WriteLine(TickJsonSerializer.ToJson(tick));
				writer.Flush();
			}
			catch (Exception ex)
			{
				log("[Intent.Stream] send failed: " + ex.Message);
				ResetConnection();
			}
		}

		private bool EnsureConnected()
		{
			if (client != null && client.Connected && writer != null)
				return true;

			DateTime now = DateTime.UtcNow;
			if ((now - lastReconnectAttemptUtc).TotalSeconds < 1)
				return false;

			lastReconnectAttemptUtc = now;

			try
			{
				ResetConnection();
				TcpClient candidate = new TcpClient();
				candidate.NoDelay = true;
				IAsyncResult connectResult = candidate.BeginConnect(host, port, null, null);
				if (!connectResult.AsyncWaitHandle.WaitOne(ConnectTimeoutMs))
				{
					try { candidate.Close(); } catch { }
					log(string.Format("[Intent.Stream] connect to {0}:{1} timed out", host, port));
					return false;
				}

				candidate.EndConnect(connectResult);
				client = candidate;
				writer = new StreamWriter(client.GetStream(), Encoding.UTF8, 4096);
				writer.NewLine = "\n";
				log(string.Format("[Intent.Stream] connected to {0}:{1}", host, port));
				return true;
			}
			catch (Exception ex)
			{
				log("[Intent.Stream] connect failed: " + ex.Message);
				ResetConnection();
				return false;
			}
		}

		private void ResetConnection()
		{
			if (writer != null)
			{
				try
				{
					writer.Dispose();
				}
				catch
				{
				}
				writer = null;
			}

			if (client != null)
			{
				try
				{
					client.Close();
				}
				catch
				{
				}
				client = null;
			}
		}
	}
}
