using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Intent.StreamRunner
{
	internal sealed class DashboardBroadcaster : IDisposable
	{
		private readonly RunnerLogger logger;
		private readonly TcpListener listener;
		private readonly List<StreamWriter> clients;
		private readonly Thread thread;
		private volatile bool stopRequested;

		public DashboardBroadcaster(int port, RunnerLogger logger)
		{
			Port = port;
			this.logger = logger;
			clients = new List<StreamWriter>();
			if (port > 0)
			{
				listener = new TcpListener(IPAddress.Parse("127.0.0.1"), port);
				thread = new Thread(ListenLoop);
				thread.IsBackground = true;
			}
		}

		public int Port { get; private set; }

		public bool IsEnabled
		{
			get { return listener != null; }
		}

		public void Start()
		{
			if (!IsEnabled)
				return;

			listener.Start();
			thread.Start();
			logger.Info("[dashboard] http://127.0.0.1:" + Port + "/");
		}

		public void Broadcast(string json)
		{
			if (!IsEnabled || string.IsNullOrWhiteSpace(json))
				return;

			string payload = "data: " + json + "\n\n";
			lock (clients)
			{
				for (int index = clients.Count - 1; index >= 0; index--)
				{
					try
					{
						clients[index].Write(payload);
						clients[index].Flush();
					}
					catch
					{
						try
						{
							clients[index].Dispose();
						}
						catch
						{
						}

						clients.RemoveAt(index);
					}
				}
			}
		}

		public void Dispose()
		{
			stopRequested = true;
			if (listener != null)
			{
				try
				{
					listener.Stop();
				}
				catch
				{
				}
			}

			lock (clients)
			{
				for (int index = 0; index < clients.Count; index++)
				{
					try
					{
						clients[index].Dispose();
					}
					catch
					{
					}
				}
				clients.Clear();
			}
		}

		private void ListenLoop()
		{
			while (!stopRequested)
			{
				TcpClient client = null;
				try
				{
					client = listener.AcceptTcpClient();
					HandleClient(client);
				}
				catch
				{
					if (stopRequested)
						break;
					if (client != null)
						client.Dispose();
				}
			}
		}

		private void HandleClient(TcpClient client)
		{
			using (client)
			using (NetworkStream stream = client.GetStream())
			using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true, 4096, true))
			{
				string requestLine = reader.ReadLine();
				if (string.IsNullOrWhiteSpace(requestLine))
					return;

				string path = ParsePath(requestLine);
				string headerLine;
				do
				{
					headerLine = reader.ReadLine();
				}
				while (!string.IsNullOrEmpty(headerLine));

				if (string.Equals(path, "/events", StringComparison.OrdinalIgnoreCase))
				{
					HandleEvents(stream);
					client = null;
					return;
				}

				HandleDashboard(stream);
			}
		}

		private void HandleDashboard(NetworkStream stream)
		{
			string html = BuildDashboardHtml();
			byte[] body = Encoding.UTF8.GetBytes(html);
			string headers = "HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: " + body.Length + "\r\nConnection: close\r\n\r\n";
			byte[] headerBytes = Encoding.ASCII.GetBytes(headers);
			stream.Write(headerBytes, 0, headerBytes.Length);
			stream.Write(body, 0, body.Length);
			stream.Flush();
		}

		private void HandleEvents(NetworkStream stream)
		{
			string headers = "HTTP/1.1 200 OK\r\nContent-Type: text/event-stream\r\nCache-Control: no-cache\r\nConnection: keep-alive\r\n\r\n";
			byte[] headerBytes = Encoding.ASCII.GetBytes(headers);
			stream.Write(headerBytes, 0, headerBytes.Length);
			stream.Flush();

			StreamWriter writer = new StreamWriter(stream, Encoding.UTF8, 4096);
			writer.AutoFlush = true;
			writer.Write(": connected\n\n");
			lock (clients)
				clients.Add(writer);
		}

		private static string ParsePath(string requestLine)
		{
			string[] parts = requestLine.Split(' ');
			if (parts.Length < 2)
				return "/";
			return string.IsNullOrWhiteSpace(parts[1]) ? "/" : parts[1];
		}

		private static string BuildDashboardHtml()
		{
			return @"<!doctype html>
<html>
<head>
  <meta charset='utf-8'>
  <meta name='viewport' content='width=device-width, initial-scale=1'>
  <title>Intent Dashboard</title>
  <style>
    :root { --bg:#0f1419; --panel:#182028; --text:#ecf2f8; --muted:#8ea1b3; --bull:#2b8a57; --bear:#c04a4a; --neutral:#587086; --line:#2a3947; }
    body { margin:0; font:14px/1.4 Consolas, monospace; background:linear-gradient(180deg,#0b1015,#111a22); color:var(--text); }
    .wrap { max-width:1200px; margin:0 auto; padding:24px; }
    .grid { display:grid; grid-template-columns:repeat(auto-fit,minmax(180px,1fr)); gap:12px; margin-bottom:16px; }
    .card { background:rgba(24,32,40,.92); border:1px solid var(--line); border-radius:12px; padding:14px; }
    .label { color:var(--muted); font-size:12px; margin-bottom:6px; text-transform:uppercase; letter-spacing:.08em; }
    .value { font-size:28px; font-weight:700; }
    .stream { display:grid; grid-template-columns:1.2fr .8fr; gap:12px; }
    .list { max-height:70vh; overflow:auto; }
    table { width:100%; border-collapse:collapse; }
    th,td { padding:8px 10px; border-bottom:1px solid var(--line); text-align:left; vertical-align:top; }
    th { position:sticky; top:0; background:#182028; }
    .bull { color:#7de2a9; }
    .bear { color:#ff8b8b; }
    .neutral { color:#9fb2c3; }
    pre { margin:0; white-space:pre-wrap; word-break:break-word; color:#cfe0ef; }
    @media (max-width:900px){ .stream{grid-template-columns:1fr;} .list{max-height:45vh;} }
  </style>
</head>
<body>
  <div class='wrap'>
    <div class='grid'>
      <div class='card'><div class='label'>Connection</div><div class='value' id='status'>Waiting</div></div>
      <div class='card'><div class='label'>Packets</div><div class='value' id='packets'>0</div></div>
      <div class='card'><div class='label'>Signals</div><div class='value' id='signals'>0</div></div>
      <div class='card'><div class='label'>Last Score</div><div class='value' id='score'>0</div></div>
      <div class='card'><div class='label'>Last Direction</div><div class='value' id='direction'>Neutral</div></div>
      <div class='card'><div class='label'>Latency ms</div><div class='value' id='latency'>0</div></div>
    </div>
    <div class='stream'>
      <div class='card list'>
        <table>
          <thead><tr><th>Time</th><th>Event</th><th>Direction</th><th>Score</th><th>Reason</th></tr></thead>
          <tbody id='rows'></tbody>
        </table>
      </div>
      <div class='card list'>
        <div class='label'>Latest Packet</div>
        <pre id='json'>{}</pre>
      </div>
    </div>
  </div>
  <script>
    const statusEl = document.getElementById('status');
    const packetsEl = document.getElementById('packets');
    const signalsEl = document.getElementById('signals');
    const scoreEl = document.getElementById('score');
    const directionEl = document.getElementById('direction');
    const latencyEl = document.getElementById('latency');
    const rowsEl = document.getElementById('rows');
    const jsonEl = document.getElementById('json');
    let packets = 0, signals = 0;
    const source = new EventSource('/events');
    source.onopen = () => statusEl.textContent = 'Connected';
    source.onerror = () => statusEl.textContent = 'Reconnecting';
    source.onmessage = (event) => {
      const packet = JSON.parse(event.data);
      packets++;
      if (packet.eventType === 'signal') signals++;
      packetsEl.textContent = packets;
      signalsEl.textContent = signals;
      scoreEl.textContent = Number(packet.score || 0).toFixed(1);
      directionEl.textContent = packet.direction || 'Neutral';
      directionEl.className = ((packet.direction || '').toLowerCase().indexOf('bull') >= 0) ? 'value bull' : ((packet.direction || '').toLowerCase().indexOf('bear') >= 0 ? 'value bear' : 'value neutral');
      latencyEl.textContent = Number(packet.latencyMs || 0).toFixed(2);
      jsonEl.textContent = JSON.stringify(packet, null, 2);
      const tr = document.createElement('tr');
      const dirClass = ((packet.direction || '').toLowerCase().indexOf('bull') >= 0) ? 'bull' : ((packet.direction || '').toLowerCase().indexOf('bear') >= 0 ? 'bear' : 'neutral');
      tr.innerHTML = `<td>${packet.timestampUtc || ''}</td><td>${packet.eventType || ''}</td><td class='${dirClass}'>${packet.direction || ''}</td><td>${Number(packet.score || 0).toFixed(1)}</td><td>${packet.dominantReason || ''}</td>`;
      rowsEl.insertBefore(tr, rowsEl.firstChild);
      while (rowsEl.children.length > 200) rowsEl.removeChild(rowsEl.lastChild);
    };
  </script>
</body>
</html>";
		}
	}
}
