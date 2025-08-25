using Serilog;
using System;
using System.Linq;

namespace ObserverBot.Services
{
    public sealed class HeartbeatService : IDisposable
    {
        private readonly MetricsService _metrics;
        private readonly ILogger _log;
        private System.Threading.Timer? _timer;

        public HeartbeatService(MetricsService metrics, ILogger log)
        {
            _metrics = metrics;
            _log = log;
        }

        public void Start(TimeSpan period)
        {
            _timer = new System.Threading.Timer(_ => Tick(), null, period, period);
        }

        private void Tick()
        {
            var (ws, ticker, symbols, recos, tf) = _metrics.SnapshotAndReset();
            var sample = string.Join(", ", tf.Take(10).Select(kv => $"{kv.k}:{kv.v}"));
            _log.Information("[HB] ws:{Ws}/int  ticker:{Tk}/int  symbols:{Sm}/int  recos:{Rc}/int  tfBars:{Count} {Sample}",
                ws, ticker, symbols, recos, tf.Length, sample);
        }

        public void Dispose() => _timer?.Dispose();
    }
}
