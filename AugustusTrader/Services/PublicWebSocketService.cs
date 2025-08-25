using Serilog;
using System;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ObserverBot.Services
{
    public sealed class PublicWebSocketService
    {
        private readonly ILogger _log;
        private readonly MetricsService _metrics;
        private readonly Uri _endpoint = new("wss://ws.poloniex.com/ws/public");
        private static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan ReconnectMin = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan ReconnectMax = TimeSpan.FromSeconds(30);

        public event Action? OnConnected;
        public event Action<WebSocketCloseStatus?, string?>? OnDisconnected;
        public event Action<Exception>? OnError;
        public event Action<string, JsonElement>? OnSymbols;
        public event Action<JsonElement>? OnTicker;

        public PublicWebSocketService(ILogger logger, MetricsService metrics)
        {
            _log = logger;
            _metrics = metrics;
        }

        public async Task RunAsync(CancellationToken ct)
        {
            var rnd = new Random();
            int attempt = 0;

            while (!ct.IsCancellationRequested)
            {
                using var cws = new ClientWebSocket();
                try
                {
                    _log.Information("[WS] Connecting {Endpoint}", _endpoint);
                    await cws.ConnectAsync(_endpoint, ct);
                    OnConnected?.Invoke();

                    await SendTextAsync(cws, "{\"event\":\"subscribe\",\"channel\":[\"ticker\"],\"symbols\":[\"all\"]}", ct);
                    await SendTextAsync(cws, "{\"event\":\"subscribe\",\"channel\":[\"symbols\"],\"symbols\":[\"all\"]}", ct);
                    _log.Information("[WS] Subscribed to ticker(all)+symbols");

                    using var ctsPing = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    var pingTask = Task.Run(() => PingLoop(cws, ctsPing.Token));

                    await ReceiveLoop(cws, ct);
                    ctsPing.Cancel();
                    attempt = 0;
                }
                catch (Exception ex)
                {
                    OnError?.Invoke(ex);
                    attempt++;
                    var backoff = TimeSpan.FromMilliseconds(
                        Math.Min(ReconnectMax.TotalMilliseconds,
                                 ReconnectMin.TotalMilliseconds * Math.Pow(2, attempt)));
                    var jitter = TimeSpan.FromMilliseconds(rnd.Next(250, 1000));
                    _log.Warning("[WS] reconnecting in {Delay}", backoff + jitter);
                    try { await Task.Delay(backoff + jitter, ct); } catch { }
                }
            }
        }

        private async Task SendTextAsync(ClientWebSocket cws, string json, CancellationToken ct)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            await cws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
        }

        private async Task PingLoop(ClientWebSocket cws, CancellationToken ct)
        {
            var ping = Encoding.UTF8.GetBytes("{\"event\":\"ping\"}");
            while (!ct.IsCancellationRequested && cws.State == WebSocketState.Open)
            {
                try
                {
                    await Task.Delay(PingInterval, ct);
                    await cws.SendAsync(ping, WebSocketMessageType.Text, true, ct);
                }
                catch { }
            }
        }

        private async Task ReceiveLoop(ClientWebSocket cws, CancellationToken ct)
        {
            var buffer = new byte[1 << 16];
            var sb = new StringBuilder();
            while (!ct.IsCancellationRequested && cws.State == WebSocketState.Open)
            {
                sb.Clear();
                WebSocketReceiveResult? result;
                do
                {
                    result = await cws.ReceiveAsync(buffer, ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        OnDisconnected?.Invoke(cws.CloseStatus, cws.CloseStatusDescription);
                        await cws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", ct);
                        return;
                    }
                    sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                } while (!result.EndOfMessage);

                var msg = sb.ToString();
                if (string.IsNullOrWhiteSpace(msg)) continue;
                _metrics.IncWsMsg();
                HandleMessage(msg);
            }
        }

        private void HandleMessage(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("event", out var evt))
                {
                    var ev = evt.GetString();
                    if (ev is "pong" or "subscribe" or "UNSUBSCRIBE" or "UNSUBSCRIBE_ALL")
                        return;
                }

                if (!root.TryGetProperty("channel", out var chProp)) return;
                var channel = chProp.GetString();

                if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return;

                switch (channel)
                {
                    case "ticker":
                        _metrics.IncTicker();
                        foreach (var item in data.EnumerateArray())
                            OnTicker?.Invoke(item);
                        break;
                    case "symbols":
                        _metrics.IncSymbols();
                        var action = root.TryGetProperty("action", out var act) ? (act.GetString() ?? "unknown") : "unknown";
                        OnSymbols?.Invoke(action, data);
                        break;
                }
            }
            catch (Exception ex)
            {
                OnError?.Invoke(ex);
            }
        }
    }
}
