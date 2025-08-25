using System;
using System.Collections.Concurrent;

namespace ObserverBot.Services
{
    public sealed class MetricsService
    {
        private long _wsMsgs, _tickerMsgs, _symbolsMsgs, _recommendations;
        private readonly ConcurrentDictionary<string, long> _tfBars = new(StringComparer.OrdinalIgnoreCase);

        public void IncWsMsg() => System.Threading.Interlocked.Increment(ref _wsMsgs);
        public void IncTicker() => System.Threading.Interlocked.Increment(ref _tickerMsgs);
        public void IncSymbols() => System.Threading.Interlocked.Increment(ref _symbolsMsgs);
        public void IncReco() => System.Threading.Interlocked.Increment(ref _recommendations);
        public void IncTfBar(string key) => _tfBars.AddOrUpdate(key, 1, (_, c) => c + 1);

        public (long ws, long ticker, long symbols, long recos, (string k, long v)[] tf) SnapshotAndReset()
        {
            var ws = System.Threading.Interlocked.Exchange(ref _wsMsgs, 0);
            var tk = System.Threading.Interlocked.Exchange(ref _tickerMsgs, 0);
            var sm = System.Threading.Interlocked.Exchange(ref _symbolsMsgs, 0);
            var rc = System.Threading.Interlocked.Exchange(ref _recommendations, 0);
            var arr = _tfBars.ToArray();
            _tfBars.Clear();
            return (ws, tk, sm, rc, Array.ConvertAll(arr, kv => (kv.Key, kv.Value)));
        }
    }
}
