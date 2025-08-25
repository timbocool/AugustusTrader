using Serilog;
using System;
using System.Collections.Concurrent;
using System.Text.Json;

namespace ObserverBot.Services
{
    public sealed class SymbolCatalogService
    {
        private readonly ILogger _log;
        private readonly ConcurrentDictionary<string, bool> _isTrading = new(StringComparer.OrdinalIgnoreCase);

        public SymbolCatalogService(ILogger log) { _log = log; }

        public (int updated, int total) ApplyUpdate(string action, JsonElement dataArray)
        {
            int updated = 0, total = 0;
            foreach (var s in dataArray.EnumerateArray())
            {
                total++;
                var symbol = s.GetProperty("symbol").GetString() ?? "";
                var state = s.TryGetProperty("state", out var st) ? (st.GetString() ?? "") : "";
                if (!string.IsNullOrEmpty(symbol) && !string.IsNullOrEmpty(state))
                {
                    bool isTrading = state.Equals("NORMAL", StringComparison.OrdinalIgnoreCase);
                    _isTrading[symbol] = isTrading;
                    updated++;
                }
            }
            return (updated, total);
        }

        public bool IsTrading(string symbol)
            => _isTrading.TryGetValue(symbol, out var ok) && ok;
    }
}
