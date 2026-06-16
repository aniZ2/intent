using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Intent.StreamRunner
{
	internal sealed class DashboardBroadcaster : IDisposable
	{
		private const string DashboardControlFileName = "intent-dashboard-control.txt";
		private const string DashboardStatusFileName = "intent-dashboard-status.json";
		private const string DashboardTokenFileName = "intent-dashboard-token.txt";
		private const int ClientReadTimeoutMs = 3000;
		private readonly RunnerLogger logger;
		private readonly TcpListener listener;
		private readonly List<SseClient> clients;
		private readonly Thread thread;
		private volatile bool stopRequested;
		private long nextCommandId;
		private readonly List<string> pendingCommands = new List<string>();
		private readonly object commandLock = new object();
		private readonly string sessionToken;
		private string latestStatusJson = string.Empty;
		private static readonly string DefaultStatusJson = "{\"connected\":false,\"mode\":\"Unknown\",\"executionEnabled\":false,\"position\":\"Flat\"}";
		private static readonly object fileLock = new object();
		private readonly object broadcastLock = new object();

		private sealed class SseClient
		{
			public StreamWriter Writer;
			public TcpClient Connection;
		}

		public DashboardBroadcaster(int port, RunnerLogger logger)
		{
			Port = port;
			this.logger = logger;
			clients = new List<SseClient>();
			sessionToken = Guid.NewGuid().ToString("N");
			if (port > 0)
			{
				// Per-session shared secret: written to a user-temp file that the local strategy reads.
				// Command/status endpoints require it, which blocks unauthenticated local processes and
				// (together with the Host/Origin checks) cross-site/DNS-rebinding browser callers.
				WriteAtomicText(GetDashboardTokenPath(), sessionToken);
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
			lock (broadcastLock)
			{
				SseClient[] snapshot;
				lock (clients)
				{
					snapshot = clients.ToArray();
				}

				List<SseClient> dead = null;
				for (int index = 0; index < snapshot.Length; index++)
				{
					try
					{
						snapshot[index].Writer.Write(payload);
						snapshot[index].Writer.Flush();
					}
					catch
					{
						if (dead == null)
							dead = new List<SseClient>();
						dead.Add(snapshot[index]);
					}
				}

				if (dead != null)
				{
					lock (clients)
					{
						for (int index = 0; index < dead.Count; index++)
							clients.Remove(dead[index]);
					}

					for (int index = 0; index < dead.Count; index++)
						DisposeSseClient(dead[index]);
				}
			}
		}

		private static void DisposeSseClient(SseClient client)
		{
			try { client.Writer.Dispose(); } catch { }
			try { client.Connection.Dispose(); } catch { }
		}

		public void Dispose()
		{
			stopRequested = true;
			try
			{
				string tokenPath = GetDashboardTokenPath();
				if (File.Exists(tokenPath))
					File.Delete(tokenPath);
			}
			catch
			{
			}

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
					DisposeSseClient(clients[index]);
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
					client.ReceiveTimeout = ClientReadTimeoutMs;
					client.SendTimeout = ClientReadTimeoutMs;
					TcpClient captured = client;
					client = null;
					ThreadPool.QueueUserWorkItem(_ =>
					{
						try
						{
							HandleClient(captured);
						}
						catch
						{
							try { captured.Dispose(); } catch { }
						}
					});
				}
				catch
				{
					if (stopRequested)
						break;
					if (client != null)
					{
						try { client.Dispose(); } catch { }
					}
				}
			}
		}

		private void HandleClient(TcpClient client)
		{
			NetworkStream stream = null;
			StreamReader reader = null;
			bool keepOpen = false;
			try
			{
				stream = client.GetStream();
				reader = new StreamReader(stream, Encoding.UTF8, true, 4096, true);
				string requestLine = reader.ReadLine();
				if (string.IsNullOrWhiteSpace(requestLine))
					return;

				string method = ParseMethod(requestLine);
				string path = ParsePath(requestLine);
				int contentLength = 0;
				string hostHeader = string.Empty;
				string originHeader = string.Empty;
				string tokenHeader = string.Empty;
				string headerLine;
				do
				{
					headerLine = reader.ReadLine();
					if (string.IsNullOrEmpty(headerLine))
						break;
					if (headerLine.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
						int.TryParse(headerLine.Substring("Content-Length:".Length).Trim(), out contentLength);
					else if (headerLine.StartsWith("Host:", StringComparison.OrdinalIgnoreCase))
						hostHeader = headerLine.Substring("Host:".Length).Trim();
					else if (headerLine.StartsWith("Origin:", StringComparison.OrdinalIgnoreCase))
						originHeader = headerLine.Substring("Origin:".Length).Trim();
					else if (headerLine.StartsWith("X-Intent-Token:", StringComparison.OrdinalIgnoreCase))
						tokenHeader = headerLine.Substring("X-Intent-Token:".Length).Trim();
				}
				while (true);

				if (string.Equals(path, "/events", StringComparison.OrdinalIgnoreCase))
				{
					HandleEvents(stream, client);
					keepOpen = true;
					return;
				}

				if (string.Equals(path, "/api/status", StringComparison.OrdinalIgnoreCase))
				{
					HandleStatus(stream);
					return;
				}

				// Endpoints below carry trade authority (commands the strategy executes, or status from
				// the strategy). They require the per-session token and a loopback Host / non-cross-site
				// Origin, so an unauthenticated local process or a malicious browser tab cannot drive them.
				if (string.Equals(path, "/api/command", StringComparison.OrdinalIgnoreCase))
				{
					if (!IsAuthorized(hostHeader, originHeader, tokenHeader))
					{
						WriteForbidden(stream);
						return;
					}

					HandleCommand(stream);
					return;
				}

				int safeContentLength = Math.Min(Math.Max(0, contentLength), 65536);

				if (string.Equals(path, "/api/control", StringComparison.OrdinalIgnoreCase) && string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
				{
					string controlBody = ReadBody(reader, safeContentLength);
					if (!IsAuthorized(hostHeader, originHeader, tokenHeader))
					{
						WriteForbidden(stream);
						return;
					}

					HandleControl(stream, controlBody);
					return;
				}

				if (string.Equals(path, "/api/strategy-status", StringComparison.OrdinalIgnoreCase) && string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
				{
					string statusBody = ReadBody(reader, safeContentLength);
					if (!IsAuthorized(hostHeader, originHeader, tokenHeader))
					{
						WriteForbidden(stream);
						return;
					}

					HandleStrategyStatus(stream, statusBody);
					return;
				}

				HandleDashboard(stream);
			}
			finally
			{
				if (reader != null)
					reader.Dispose();

				if (!keepOpen)
				{
					if (stream != null)
						stream.Dispose();
					client.Dispose();
				}
			}
		}

		private static string ReadBody(StreamReader reader, int length)
		{
			if (length <= 0)
				return string.Empty;

			char[] buffer = new char[length];
			int totalRead = 0;
			while (totalRead < buffer.Length)
			{
				int read = reader.Read(buffer, totalRead, buffer.Length - totalRead);
				if (read <= 0)
					break;
				totalRead += read;
			}

			return new string(buffer, 0, totalRead);
		}

		private void HandleDashboard(NetworkStream stream)
		{
			string html = BuildDashboardHtml();
			WriteResponse(stream, "text/html; charset=utf-8", html);
		}

		private void HandleEvents(NetworkStream stream, TcpClient connection)
		{
			string headers = "HTTP/1.1 200 OK\r\nContent-Type: text/event-stream\r\nCache-Control: no-cache\r\nConnection: keep-alive\r\n\r\n";
			byte[] headerBytes = Encoding.ASCII.GetBytes(headers);
			stream.Write(headerBytes, 0, headerBytes.Length);
			stream.Flush();

			connection.SendTimeout = ClientReadTimeoutMs;
			StreamWriter writer = new StreamWriter(stream, Encoding.UTF8, 4096);
			writer.AutoFlush = true;
			writer.Write(": connected\n\n");
			lock (clients)
				clients.Add(new SseClient { Writer = writer, Connection = connection });
		}

		private void HandleStatus(NetworkStream stream)
		{
			string payload = string.IsNullOrWhiteSpace(latestStatusJson)
				? ReadDashboardStatusJson()
				: latestStatusJson;
			if (string.IsNullOrWhiteSpace(payload))
				payload = DefaultStatusJson;
			WriteResponse(stream, "application/json; charset=utf-8", payload);
		}

		private void HandleCommand(NetworkStream stream)
		{
			string payload;
			lock (commandLock)
			{
				payload = pendingCommands.Count == 0 ? string.Empty : string.Join("\n", pendingCommands.ToArray());
				pendingCommands.Clear();
			}

			WriteResponse(stream, "text/plain; charset=utf-8", payload);
		}

		private void HandleControl(NetworkStream stream, string body)
		{
			string command = BuildCommandPayload(body);
			lock (commandLock)
			{
				// FIFO queue (not a single slot) so two rapidly-issued commands are not coalesced/dropped.
				pendingCommands.Add(command);
				while (pendingCommands.Count > 64)
					pendingCommands.RemoveAt(0);
			}

			WriteAtomicText(GetDashboardControlPath(), command);
			long commandId = ParseCommandId(command);
			WriteResponse(stream, "application/json; charset=utf-8", "{\"ok\":true,\"commandId\":" + commandId.ToString(CultureInfo.InvariantCulture) + "}");
		}

		private void HandleStrategyStatus(NetworkStream stream, string body)
		{
			string payload = string.IsNullOrWhiteSpace(body)
				? DefaultStatusJson
				: body.Trim();
			latestStatusJson = payload;
			WriteAtomicText(GetDashboardStatusPath(), payload);
			WriteResponse(stream, "application/json; charset=utf-8", "{\"ok\":true}");
		}

		private static void WriteResponse(NetworkStream stream, string contentType, string body)
		{
			byte[] payload = Encoding.UTF8.GetBytes(body ?? string.Empty);
			string headers = "HTTP/1.1 200 OK\r\nContent-Type: " + contentType + "\r\nContent-Length: " + payload.Length + "\r\nConnection: close\r\n\r\n";
			byte[] headerBytes = Encoding.ASCII.GetBytes(headers);
			stream.Write(headerBytes, 0, headerBytes.Length);
			stream.Write(payload, 0, payload.Length);
			stream.Flush();
		}

		private static string ParseMethod(string requestLine)
		{
			string[] parts = requestLine.Split(' ');
			return parts.Length > 0 ? parts[0] : "GET";
		}

		private static string ParsePath(string requestLine)
		{
			string[] parts = requestLine.Split(' ');
			if (parts.Length < 2)
				return "/";
			return string.IsNullOrWhiteSpace(parts[1]) ? "/" : parts[1];
		}

		private string BuildCommandPayload(string body)
		{
			long id = Interlocked.Increment(ref nextCommandId);
			string timestampUtc = DateTime.UtcNow.ToString("o");
			string normalizedBody = string.IsNullOrWhiteSpace(body) ? string.Empty : body.Trim();
			if (!string.IsNullOrEmpty(normalizedBody) && normalizedBody[0] == '?')
				normalizedBody = normalizedBody.Substring(1);

			if (!string.IsNullOrEmpty(normalizedBody))
				return "id=" + id + "&timestampUtc=" + Uri.EscapeDataString(timestampUtc) + "&" + normalizedBody;

			return "id=" + id + "&timestampUtc=" + Uri.EscapeDataString(timestampUtc);
		}

		private static long ParseCommandId(string command)
		{
			if (string.IsNullOrWhiteSpace(command))
				return 0;

			string[] parts = command.Split('&');
			for (int index = 0; index < parts.Length; index++)
			{
				string[] pair = parts[index].Split(new[] { '=' }, 2);
				if (pair.Length == 2 && string.Equals(pair[0], "id", StringComparison.OrdinalIgnoreCase))
				{
					long parsed;
					return long.TryParse(pair[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) ? parsed : 0;
				}
			}

			return 0;
		}

		private static string ReadDashboardStatusJson()
		{
			string path = GetDashboardStatusPath();
			if (!File.Exists(path))
				return string.Empty;

			try
			{
				string payload = File.ReadAllText(path, Encoding.UTF8);
				return string.IsNullOrWhiteSpace(payload)
					? string.Empty
					: payload;
			}
			catch
			{
				return string.Empty;
			}
		}

		private static string GetDashboardControlPath()
		{
			return Path.Combine(Path.GetTempPath(), DashboardControlFileName);
		}

		private static string GetDashboardStatusPath()
		{
			return Path.Combine(Path.GetTempPath(), DashboardStatusFileName);
		}

		private static string GetDashboardTokenPath()
		{
			return Path.Combine(Path.GetTempPath(), DashboardTokenFileName);
		}

		private bool IsAuthorized(string host, string origin, string token)
		{
			if (!IsLoopbackHost(host))
				return false;
			if (!string.IsNullOrEmpty(origin) && !IsLoopbackOrigin(origin))
				return false;
			return !string.IsNullOrEmpty(sessionToken) && string.Equals(token, sessionToken, StringComparison.Ordinal);
		}

		private static bool IsLoopbackHost(string host)
		{
			if (string.IsNullOrEmpty(host))
				return false;
			string h = host.Trim().ToLowerInvariant();
			return h.StartsWith("127.0.0.1") || h.StartsWith("localhost") || h.StartsWith("[::1]");
		}

		private static bool IsLoopbackOrigin(string origin)
		{
			string o = origin.Trim().ToLowerInvariant();
			return o.Contains("127.0.0.1") || o.Contains("localhost") || o.Contains("[::1]");
		}

		private static void WriteForbidden(NetworkStream stream)
		{
			byte[] payload = Encoding.UTF8.GetBytes("{\"ok\":false,\"error\":\"forbidden\"}");
			string headers = "HTTP/1.1 403 Forbidden\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: " + payload.Length + "\r\nConnection: close\r\n\r\n";
			byte[] headerBytes = Encoding.ASCII.GetBytes(headers);
			stream.Write(headerBytes, 0, headerBytes.Length);
			stream.Write(payload, 0, payload.Length);
			stream.Flush();
		}

		private static void WriteAtomicText(string path, string content)
		{
			lock (fileLock)
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
		}

		private string BuildDashboardHtml()
		{
			string html = @"<!doctype html>
<html>
<head>
  <meta charset='utf-8'>
  <meta name='viewport' content='width=device-width, initial-scale=1'>
  <title>Intent Dashboard</title>
  <style>
    :root { --bg:#0f1419; --panel:#182028; --text:#ecf2f8; --muted:#8ea1b3; --bull:#2b8a57; --bear:#c04a4a; --neutral:#587086; --line:#2a3947; --accent:#f0c04d; }
    body { margin:0; font:14px/1.4 Consolas, monospace; background:linear-gradient(180deg,#0b1015,#111a22); color:var(--text); }
    .wrap { max-width:1200px; margin:0 auto; padding:24px; }
    .grid { display:grid; grid-template-columns:repeat(auto-fit,minmax(180px,1fr)); gap:12px; margin-bottom:16px; }
    .card { background:rgba(24,32,40,.92); border:1px solid var(--line); border-radius:12px; padding:14px; }
    .label { color:var(--muted); font-size:12px; margin-bottom:6px; text-transform:uppercase; letter-spacing:.08em; }
    .value { font-size:28px; font-weight:700; }
    .value.small { font-size:18px; }
    .hero { display:grid; grid-template-columns:repeat(auto-fit,minmax(180px,1fr)); gap:12px; margin-bottom:16px; }
    .badge { display:inline-block; padding:10px 14px; border-radius:999px; font-size:16px; font-weight:700; border:1px solid var(--line); }
    .badge.ready { background:rgba(43,138,87,.18); color:#8ef0b5; border-color:#2b8a57; }
    .badge.blocked { background:rgba(192,74,74,.18); color:#ff9b9b; border-color:#c04a4a; }
    .badge.manual { background:rgba(240,192,77,.18); color:#ffe08c; border-color:#f0c04d; }
    .stream { display:grid; grid-template-columns:1.2fr .8fr; gap:12px; }
    .list { max-height:70vh; overflow:auto; }
    .controls { display:grid; grid-template-columns:repeat(auto-fit,minmax(120px,1fr)); gap:10px; margin-bottom:16px; }
    button { width:100%; padding:12px 10px; border-radius:10px; border:1px solid var(--line); background:#111922; color:var(--text); font:600 13px Consolas, monospace; cursor:pointer; }
    button:hover { border-color:var(--accent); color:#fff6d3; }
    button.active { border-color:var(--accent); background:rgba(240,192,77,.12); color:#fff6d3; box-shadow:inset 0 0 0 1px rgba(240,192,77,.45); }
    button.good { border-color:#2b8a57; color:#8ef0b5; }
    button.bad { border-color:#c04a4a; color:#ff9b9b; }
    .form-grid { display:grid; grid-template-columns:repeat(auto-fit,minmax(180px,1fr)); gap:10px; margin-top:12px; }
    label { display:block; color:var(--muted); font-size:12px; margin-bottom:4px; text-transform:uppercase; letter-spacing:.08em; }
    input { width:100%; box-sizing:border-box; padding:10px; border-radius:8px; border:1px solid var(--line); background:#0f1419; color:var(--text); font:13px Consolas, monospace; }
    .status-grid { display:grid; grid-template-columns:repeat(auto-fit,minmax(150px,1fr)); gap:12px; margin-bottom:16px; }
    table { width:100%; border-collapse:collapse; }
    th,td { padding:8px 10px; border-bottom:1px solid var(--line); text-align:left; vertical-align:top; }
    th { position:sticky; top:0; background:#182028; }
    .bull { color:#7de2a9; }
    .bear { color:#ff8b8b; }
    .neutral { color:#9fb2c3; }
    .accent { color:#ffe08c; }
    .good-text { color:#8ef0b5; }
    .bad-text { color:#ff9b9b; }
    pre { margin:0; white-space:pre-wrap; word-break:break-word; color:#cfe0ef; }
    @media (max-width:900px){ .stream{grid-template-columns:1fr;} .list{max-height:45vh;} }
  </style>
</head>
<body>
  <div class='wrap'>
    <div class='hero'>
      <div class='card'><div class='label'>Trade Readiness</div><div class='badge blocked' id='readinessBadge'>UNKNOWN</div></div>
      <div class='card'><div class='label'>Rule Apply</div><div class='value small' id='ruleApplyState'>Idle</div></div>
      <div class='card'><div class='label'>Last Attempt Reason</div><div class='value small' id='lastAttemptReasonCard'>n/a</div></div>
    </div>
    <div class='grid'>
      <div class='card'><div class='label'>Connection</div><div class='value' id='status'>Waiting</div></div>
      <div class='card'><div class='label'>Packets</div><div class='value' id='packets'>0</div></div>
      <div class='card'><div class='label'>Signals</div><div class='value' id='signals'>0</div></div>
      <div class='card'><div class='label'>Last Score</div><div class='value' id='score'>0</div></div>
      <div class='card'><div class='label'>Last Direction</div><div class='value' id='direction'>Neutral</div></div>
      <div class='card'><div class='label'>Latency ms</div><div class='value' id='latency'>0</div></div>
    </div>
    <div class='card'>
      <div class='label'>Controls</div>
      <div class='controls'>
        <button data-action='set_mode' data-value='manual'>Manual Only</button>
        <button data-action='set_mode' data-value='auto'>Auto Trade</button>
        <button data-action='set_execution' data-value='enabled'>Execution Armed</button>
        <button data-action='set_execution' data-value='disabled'>Execution Blocked</button>
        <button data-action='flatten' data-value='now'>Flatten</button>
      </div>
      <div class='controls'>
        <button data-action='set_continuation_only' data-value='true'>Continuation Only</button>
        <button data-action='set_continuation_only' data-value='false'>All Signal Types</button>
      </div>
      <div class='form-grid'>
        <div>
          <label for='dashboardQuantity'>Dashboard Order Qty</label>
          <input id='dashboardQuantity' type='number' min='1' step='1' />
        </div>
      </div>
      <div class='controls'>
        <button id='buyMarket' class='good'>Buy Market</button>
        <button id='sellMarket' class='bad'>Sell Market</button>
        <button id='reversePosition'>Reverse</button>
        <button id='setQuantity'>Set Qty</button>
      </div>
      <div class='form-grid'>
        <div>
          <label for='maxTrades'>Max Trades / Session</label>
          <input id='maxTrades' type='number' min='0' step='1' />
        </div>
        <div>
          <label for='cooldownBars'>Cooldown Bars</label>
          <input id='cooldownBars' type='number' min='0' step='1' />
        </div>
        <div>
          <label for='minIntentScore'>Min Auto Intent</label>
          <input id='minIntentScore' type='number' min='1' max='100' step='1' />
        </div>
        <div>
          <label for='compressionMax'>Compression Max</label>
          <input id='compressionMax' type='number' min='0.1' max='2.0' step='0.01' />
        </div>
        <div>
          <label for='expansionMin'>Expansion Min</label>
          <input id='expansionMin' type='number' min='0.5' max='5.0' step='0.01' />
        </div>
        <div>
          <label for='volumeSpikeMin'>Volume Spike Min</label>
          <input id='volumeSpikeMin' type='number' min='0.5' max='5.0' step='0.01' />
        </div>
      </div>
      <div class='controls'>
        <button id='applyRules'>Apply Rules</button>
      </div>
    </div>
    <div class='status-grid'>
      <div class='card'><div class='label'>Strategy Mode</div><div class='value small' id='strategyMode'>Unknown</div></div>
      <div class='card'><div class='label'>Timeframes</div><div class='value small' id='timeframeMode'>Single</div></div>
      <div class='card'><div class='label' id='higherTimeframeLabel'>HTF Bias</div><div class='value small' id='higherTimeframeBias'>Unknown</div></div>
      <div class='card'><div class='label'>Execution</div><div class='value small' id='executionState'>Unknown</div></div>
      <div class='card'><div class='label'>Status Age</div><div class='value small' id='statusAge'>n/a</div></div>
      <div class='card'><div class='label'>Position</div><div class='value small' id='positionState'>Flat</div></div>
      <div class='card'><div class='label'>Current Price</div><div class='value small accent' id='currentPrice'>0</div></div>
      <div class='card'><div class='label'>Entry / Stop / Target</div><div class='value small' id='tradeLevels'>n/a</div></div>
      <div class='card'><div class='label'>Session PnL</div><div class='value small' id='sessionPnl'>0</div></div>
      <div class='card'><div class='label'>Balance</div><div class='value small' id='accountBalance'>0</div></div>
      <div class='card'><div class='label'>Realized PnL</div><div class='value small' id='realizedPnl'>0</div></div>
      <div class='card'><div class='label'>Unrealized PnL</div><div class='value small' id='unrealizedPnl'>0</div></div>
      <div class='card'><div class='label'>Lock Reason</div><div class='value small' id='lockReason'>Unknown</div></div>
      <div class='card'><div class='label'>Cooldown Bars</div><div class='value small' id='cooldownRemaining'>0</div></div>
      <div class='card'><div class='label'>Compression / Expansion</div><div class='value small' id='gateState'>N / N</div></div>
      <div class='card'><div class='label'>Continuation Only</div><div class='value small' id='continuationOnly'>Off</div></div>
      <div class='card'><div class='label'>Trades This Session</div><div class='value small' id='sessionTrades'>0</div></div>
      <div class='card'><div class='label'>Last Attempt</div><div class='value small' id='lastAttempt'>None</div></div>
      <div class='card'><div class='label'>Command Ack</div><div class='value small' id='commandAck'>None</div></div>
      <div class='card'><div class='label'>Last Order</div><div class='value small' id='lastOrder'>None</div></div>
      <div class='card'><div class='label'>Last Execution</div><div class='value small' id='lastExecution'>None</div></div>
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
    const INTENT_TOKEN = '__INTENT_TOKEN__';
    const statusEl = document.getElementById('status');
    const packetsEl = document.getElementById('packets');
    const signalsEl = document.getElementById('signals');
    const scoreEl = document.getElementById('score');
    const directionEl = document.getElementById('direction');
    const latencyEl = document.getElementById('latency');
    const rowsEl = document.getElementById('rows');
    const jsonEl = document.getElementById('json');
    const readinessBadgeEl = document.getElementById('readinessBadge');
    const ruleApplyStateEl = document.getElementById('ruleApplyState');
    const lastAttemptReasonCardEl = document.getElementById('lastAttemptReasonCard');
    const strategyModeEl = document.getElementById('strategyMode');
    const timeframeModeEl = document.getElementById('timeframeMode');
    const higherTimeframeBiasEl = document.getElementById('higherTimeframeBias');
    const higherTimeframeLabelEl = document.getElementById('higherTimeframeLabel');
    const executionStateEl = document.getElementById('executionState');
    const statusAgeEl = document.getElementById('statusAge');
    const positionStateEl = document.getElementById('positionState');
    const currentPriceEl = document.getElementById('currentPrice');
    const tradeLevelsEl = document.getElementById('tradeLevels');
    const sessionPnlEl = document.getElementById('sessionPnl');
    const accountBalanceEl = document.getElementById('accountBalance');
    const realizedPnlEl = document.getElementById('realizedPnl');
    const unrealizedPnlEl = document.getElementById('unrealizedPnl');
    const lockReasonEl = document.getElementById('lockReason');
    const cooldownRemainingEl = document.getElementById('cooldownRemaining');
    const gateStateEl = document.getElementById('gateState');
    const continuationOnlyEl = document.getElementById('continuationOnly');
    const sessionTradesEl = document.getElementById('sessionTrades');
    const lastAttemptEl = document.getElementById('lastAttempt');
    const commandAckEl = document.getElementById('commandAck');
    const lastOrderEl = document.getElementById('lastOrder');
    const lastExecutionEl = document.getElementById('lastExecution');
    const dashboardQuantityInput = document.getElementById('dashboardQuantity');
    const maxTradesInput = document.getElementById('maxTrades');
    const cooldownBarsInput = document.getElementById('cooldownBars');
    const minIntentScoreInput = document.getElementById('minIntentScore');
    const compressionMaxInput = document.getElementById('compressionMax');
    const expansionMinInput = document.getElementById('expansionMin');
    const volumeSpikeMinInput = document.getElementById('volumeSpikeMin');
    const buyMarketButton = document.getElementById('buyMarket');
    const sellMarketButton = document.getElementById('sellMarket');
    const reversePositionButton = document.getElementById('reversePosition');
    const setQuantityButton = document.getElementById('setQuantity');
    const applyRulesButton = document.getElementById('applyRules');
    let packets = 0, signals = 0;
    let lastAppliedSignature = '';
    let statusResetTimer = 0;
    let lastNonDiagnosticStatus = null;
    let pendingCommandId = 0;
    let pendingCommandAction = '';
    let focusedInputId = '';
    document.addEventListener('focusin', (e) => { if (e.target && e.target.tagName === 'INPUT') focusedInputId = e.target.id; });
    document.addEventListener('focusout', (e) => { if (e.target && e.target.id === focusedInputId) focusedInputId = ''; });

    function setActiveButtons(status) {
      document.querySelectorAll('button[data-action]').forEach((button) => {
        button.classList.remove('active', 'good', 'bad');
      });

      const mode = (status.mode || '').toLowerCase();
      const execEnabled = !!status.executionEnabled;
      const continuationOnly = !!status.tradeContinuationOnly;

      const autoButton = document.querySelector('button[data-action=""set_mode""][data-value=""auto""]');
      const manualButton = document.querySelector('button[data-action=""set_mode""][data-value=""manual""]');
      const enableButton = document.querySelector('button[data-action=""set_execution""][data-value=""enabled""]');
      const disableButton = document.querySelector('button[data-action=""set_execution""][data-value=""disabled""]');
      const continuationOnButton = document.querySelector('button[data-action=""set_continuation_only""][data-value=""true""]');
      const continuationOffButton = document.querySelector('button[data-action=""set_continuation_only""][data-value=""false""]');

      if (mode === 'auto' && autoButton) autoButton.classList.add('active');
      if (mode === 'manual' && manualButton) manualButton.classList.add('active');
      if (enableButton) enableButton.classList.add(execEnabled ? 'active' : 'good');
      if (disableButton) disableButton.classList.add(!execEnabled ? 'active' : 'bad');
      if (continuationOnButton && continuationOnly) continuationOnButton.classList.add('active');
      if (continuationOffButton && !continuationOnly) continuationOffButton.classList.add('active');
    }

    function setReadiness(status) {
      const mode = (status.mode || '').toLowerCase();
      const lockReason = status.lockReason || 'UNKNOWN';
      let text = lockReason;
      let cls = 'badge blocked';

      if (mode === 'manual') {
        text = 'MANUAL';
        cls = 'badge manual';
      } else if (lockReason === 'READY') {
        text = 'READY';
        cls = 'badge ready';
      } else if (lockReason === 'WAITING_TRIGGER' || lockReason === 'STAND_ASIDE') {
        cls = 'badge manual';
      }

      readinessBadgeEl.textContent = text;
      readinessBadgeEl.className = cls;
    }

    function setRuleApplyState(text) {
      ruleApplyStateEl.textContent = text;
      if (statusResetTimer)
        clearTimeout(statusResetTimer);
      if (text !== 'Idle') {
        statusResetTimer = setTimeout(() => {
          if (ruleApplyStateEl.textContent === text)
            ruleApplyStateEl.textContent = 'Idle';
        }, 2500);
      }
    }

    async function postControl(body) {
      const controller = new AbortController();
      const timeoutHandle = setTimeout(() => controller.abort(), 1200);
      try {
        const response = await fetch('/api/control', {
          method: 'POST',
          headers: { 'Content-Type': 'application/x-www-form-urlencoded', 'X-Intent-Token': INTENT_TOKEN },
          body,
          signal: controller.signal
        });
        if (!response.ok)
          throw new Error('control failed');
        return await response.json();
      } finally {
        clearTimeout(timeoutHandle);
      }
    }

    async function sendControl(action, value, extras) {
      const payload = new URLSearchParams();
      payload.set('action', action);
      if (typeof value !== 'undefined')
        payload.set('value', value);
      if (extras) {
        Object.keys(extras).forEach((key) => {
          if (typeof extras[key] !== 'undefined' && extras[key] !== null)
            payload.set(key, extras[key]);
        });
      }
      return await postControl(payload.toString());
    }

    async function refreshStatus() {
      try {
        const response = await fetch('/api/status', { cache: 'no-store' });
        const status = await response.json();
        const isFallbackUnknown = (status.mode || '') === 'Unknown' && !status.statusTimestampUtc;
        const effectiveStatus = ((status.diagnosticOnly || isFallbackUnknown) && lastNonDiagnosticStatus) ? lastNonDiagnosticStatus : status;
        if (!status.diagnosticOnly && !isFallbackUnknown)
          lastNonDiagnosticStatus = status;
        strategyModeEl.textContent = effectiveStatus.mode || 'Unknown';
        timeframeModeEl.textContent = effectiveStatus.timeframeMode || 'Single';
        const higherDirection = effectiveStatus.higherTimeframeDirection || 'Unknown';
        const higherScore = Number(effectiveStatus.higherTimeframeIntentScore || 0).toFixed(1);
        const htfMinutes = Number(effectiveStatus.higherTimeframeMinutes || 0);
        higherTimeframeLabelEl.textContent = htfMinutes > 0 ? `${htfMinutes}m Bias` : 'HTF Bias';
        higherTimeframeBiasEl.textContent = `${higherDirection} ${higherScore}`;
        executionStateEl.textContent = effectiveStatus.executionEnabled ? 'Enabled' : 'Disabled';
        executionStateEl.className = effectiveStatus.executionEnabled ? 'value small good-text' : 'value small bad-text';
        if (status.statusTimestampUtc) {
          const ageSeconds = Math.max(0, Math.round((Date.now() - Date.parse(status.statusTimestampUtc)) / 1000));
          statusAgeEl.textContent = ageSeconds <= 2 ? `${ageSeconds}s` : `${ageSeconds}s stale`;
          statusAgeEl.className = ageSeconds <= 2 ? 'value small good-text' : 'value small bad-text';
        } else {
          statusAgeEl.textContent = 'n/a';
          statusAgeEl.className = 'value small';
        }
        positionStateEl.textContent = effectiveStatus.position || 'Flat';
        currentPriceEl.textContent = Number(effectiveStatus.currentPrice || 0).toFixed(2);
        const entry = effectiveStatus.entryPrice > 0 ? Number(effectiveStatus.entryPrice).toFixed(2) : 'n/a';
        const stop = effectiveStatus.stopPrice > 0 ? Number(effectiveStatus.stopPrice).toFixed(2) : 'n/a';
        const target = effectiveStatus.targetPrice > 0 ? Number(effectiveStatus.targetPrice).toFixed(2) : 'n/a';
        tradeLevelsEl.textContent = `${entry} / ${stop} / ${target}`;
        sessionPnlEl.textContent = Number(effectiveStatus.sessionPnl || 0).toFixed(2);
        accountBalanceEl.textContent = Number(effectiveStatus.accountBalance || 0).toFixed(2);
        realizedPnlEl.textContent = Number(effectiveStatus.realizedPnL || 0).toFixed(2);
        unrealizedPnlEl.textContent = Number(effectiveStatus.unrealizedPnL || 0).toFixed(2);
        lockReasonEl.textContent = effectiveStatus.lockReason || 'Unknown';
        cooldownRemainingEl.textContent = Number(effectiveStatus.cooldownRemainingBars || 0).toFixed(0);
        gateStateEl.textContent = `${effectiveStatus.compressionPassed ? 'Y' : 'N'} / ${effectiveStatus.expansionPassed ? 'Y' : 'N'}`;
        continuationOnlyEl.textContent = effectiveStatus.tradeContinuationOnly ? 'On' : 'Off';
        sessionTradesEl.textContent = `${Number(effectiveStatus.sessionTradeCount || 0).toFixed(0)} / ${Number(effectiveStatus.maxTradesPerSession || 0).toFixed(0)}`;
        lastAttemptEl.textContent = `${effectiveStatus.lastAttemptAction || 'None'} / ${effectiveStatus.lastAttemptOutcome || 'None'}`;
        commandAckEl.textContent = effectiveStatus.lastCommandAcknowledgement || 'None';
        if (effectiveStatus.lastAppliedCommandId)
          commandAckEl.textContent += ` (#${effectiveStatus.lastAppliedCommandId} ${effectiveStatus.lastAppliedCommandAction || ''})`;
        lastOrderEl.textContent = effectiveStatus.lastOrderSummary || 'None';
        lastExecutionEl.textContent = effectiveStatus.lastExecutionSummary || 'None';
        lastAttemptReasonCardEl.textContent = effectiveStatus.lastAttemptReason || effectiveStatus.lockReason || 'n/a';
        if (focusedInputId !== 'dashboardQuantity') dashboardQuantityInput.value = Number(effectiveStatus.dashboardOrderQuantity || 1).toFixed(0);
        if (focusedInputId !== 'maxTrades') maxTradesInput.value = Number(effectiveStatus.maxTradesPerSession || 0).toFixed(0);
        if (focusedInputId !== 'cooldownBars') cooldownBarsInput.value = Number(effectiveStatus.cooldownBars || 0).toFixed(0);
        if (focusedInputId !== 'minIntentScore') minIntentScoreInput.value = Number(effectiveStatus.minAutoIntentScore || 0).toFixed(0);
        if (focusedInputId !== 'compressionMax') compressionMaxInput.value = Number(effectiveStatus.compressionRangeExpansionMax || 0).toFixed(2);
        if (focusedInputId !== 'expansionMin') expansionMinInput.value = Number(effectiveStatus.expansionRangeExpansionMin || 0).toFixed(2);
        if (focusedInputId !== 'volumeSpikeMin') volumeSpikeMinInput.value = Number(effectiveStatus.expansionVolumeSpikeMin || 0).toFixed(2);
        setActiveButtons(effectiveStatus);
        setReadiness(effectiveStatus);

        const appliedCommandId = Number(effectiveStatus.lastAppliedCommandId || 0);
        if (pendingCommandId > 0) {
          if (appliedCommandId >= pendingCommandId) {
            setRuleApplyState(`Applied #${appliedCommandId}`);
            pendingCommandId = 0;
            pendingCommandAction = '';
          } else {
            setRuleApplyState(`Awaiting Ack #${pendingCommandId}${pendingCommandAction ? ' ' + pendingCommandAction : ''}`);
          }
        }

        const currentSignature = [
          maxTradesInput.value,
          cooldownBarsInput.value,
          minIntentScoreInput.value,
          compressionMaxInput.value,
          expansionMinInput.value,
          volumeSpikeMinInput.value,
          continuationOnlyEl.textContent
        ].join('|');
        if (lastAppliedSignature && currentSignature === lastAppliedSignature)
          setRuleApplyState('Applied');
      } catch (err) {
        if (ruleApplyStateEl.textContent !== 'Applied')
          setRuleApplyState('Status Error');
      }
    }

    document.querySelectorAll('button[data-action]').forEach((button) => {
      button.addEventListener('click', async () => {
        if (button.dataset.action === 'flatten' && !confirm('FLATTEN all positions?')) return;
        setRuleApplyState('Sending...');
        try {
          const result = await sendControl(button.dataset.action, button.dataset.value || '');
          pendingCommandId = Number(result.commandId || 0);
          pendingCommandAction = button.dataset.action || '';
          if (button.dataset.action === 'set_continuation_only')
            lastAppliedSignature = '';
          setRuleApplyState(pendingCommandId > 0 ? `Awaiting Ack #${pendingCommandId}` : 'Sent');
        } catch (err) {
          setRuleApplyState('Send Failed');
        } finally {
          setTimeout(refreshStatus, 150);
        }
      });
    });

    buyMarketButton.addEventListener('click', async () => {
      if (!confirm('Submit BUY MARKET order?')) return;
      setRuleApplyState('Submitting Buy...');
      try {
        const result = await sendControl('buy_market', 'now', { quantity: dashboardQuantityInput.value || '1' });
        pendingCommandId = Number(result.commandId || 0);
        pendingCommandAction = 'buy_market';
        setRuleApplyState(pendingCommandId > 0 ? `Awaiting Ack #${pendingCommandId} buy` : 'Buy Sent');
      } catch (err) {
        setRuleApplyState('Buy Failed');
      } finally {
        setTimeout(refreshStatus, 150);
      }
    });

    sellMarketButton.addEventListener('click', async () => {
      if (!confirm('Submit SELL MARKET order?')) return;
      setRuleApplyState('Submitting Sell...');
      try {
        const result = await sendControl('sell_market', 'now', { quantity: dashboardQuantityInput.value || '1' });
        pendingCommandId = Number(result.commandId || 0);
        pendingCommandAction = 'sell_market';
        setRuleApplyState(pendingCommandId > 0 ? `Awaiting Ack #${pendingCommandId} sell` : 'Sell Sent');
      } catch (err) {
        setRuleApplyState('Sell Failed');
      } finally {
        setTimeout(refreshStatus, 150);
      }
    });

    reversePositionButton.addEventListener('click', async () => {
      if (!confirm('Submit REVERSE order?')) return;
      setRuleApplyState('Submitting Reverse...');
      try {
        const result = await sendControl('reverse', 'now', { quantity: dashboardQuantityInput.value || '1' });
        pendingCommandId = Number(result.commandId || 0);
        pendingCommandAction = 'reverse';
        setRuleApplyState(pendingCommandId > 0 ? `Awaiting Ack #${pendingCommandId} reverse` : 'Reverse Sent');
      } catch (err) {
        setRuleApplyState('Reverse Failed');
      } finally {
        setTimeout(refreshStatus, 150);
      }
    });

    setQuantityButton.addEventListener('click', async () => {
      setRuleApplyState('Saving Qty...');
      try {
        const result = await sendControl('set_dashboard_quantity', dashboardQuantityInput.value || '1', { quantity: dashboardQuantityInput.value || '1' });
        pendingCommandId = Number(result.commandId || 0);
        pendingCommandAction = 'set_dashboard_quantity';
        setRuleApplyState(pendingCommandId > 0 ? `Awaiting Ack #${pendingCommandId} qty` : 'Qty Saved');
      } catch (err) {
        setRuleApplyState('Qty Failed');
      } finally {
        setTimeout(refreshStatus, 150);
      }
    });

    applyRulesButton.addEventListener('click', async () => {
      setRuleApplyState('Applying...');
      const payload = [
        `max_trades_per_session=${encodeURIComponent(maxTradesInput.value || '0')}`,
        `cooldown_bars=${encodeURIComponent(cooldownBarsInput.value || '0')}`,
        `min_auto_intent_score=${encodeURIComponent(minIntentScoreInput.value || '60')}`,
        `compression_range_expansion_max=${encodeURIComponent(compressionMaxInput.value || '0.85')}`,
        `expansion_range_expansion_min=${encodeURIComponent(expansionMinInput.value || '1.15')}`,
        `expansion_volume_spike_min=${encodeURIComponent(volumeSpikeMinInput.value || '1.10')}`
      ].join('&');
      lastAppliedSignature = [
        maxTradesInput.value || '0',
        cooldownBarsInput.value || '0',
        minIntentScoreInput.value || '60',
        compressionMaxInput.value || '0.85',
        expansionMinInput.value || '1.15',
        volumeSpikeMinInput.value || '1.10',
        continuationOnlyEl.textContent
      ].join('|');
      try {
        const result = await postControl(`action=update_rules&${payload}`);
        pendingCommandId = Number(result.commandId || 0);
        pendingCommandAction = 'update_rules';
        setRuleApplyState(pendingCommandId > 0 ? `Awaiting Ack #${pendingCommandId} rules` : 'Rules Sent');
      } catch (err) {
        setRuleApplyState('Apply Failed');
      } finally {
        setTimeout(refreshStatus, 150);
      }
    });

    const source = new EventSource('/events');
    source.onopen = () => statusEl.textContent = 'Connected';
    source.onerror = () => statusEl.textContent = 'Reconnecting';
    source.onmessage = (event) => {
      let packet;
      try { packet = JSON.parse(event.data); } catch (e) { return; }
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
      const cells = [packet.timestampUtc || '', packet.eventType || '', packet.direction || '', Number(packet.score || 0).toFixed(1), packet.dominantReason || ''];
      const classes = ['', '', dirClass, '', ''];
      cells.forEach((text, i) => { const td = document.createElement('td'); td.textContent = text; if (classes[i]) td.className = classes[i]; tr.appendChild(td); });
      rowsEl.insertBefore(tr, rowsEl.firstChild);
      while (rowsEl.children.length > 200) rowsEl.removeChild(rowsEl.lastChild);
    };
    refreshStatus();
    setInterval(refreshStatus, 1000);
  </script>
</body>
</html>";

			return html.Replace("__INTENT_TOKEN__", sessionToken);
		}
	}
}
