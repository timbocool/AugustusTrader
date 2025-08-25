using Serilog;
using System;
using ObserverBot.App;

namespace ObserverBot.Services
{
    public sealed class WatchlistService
    {
        private readonly AppConfig _cfg;
        private readonly ILogger _log;

        public WatchlistService(AppConfig cfg, ILogger log) { _cfg = cfg; _log = log; }

        public bool ShouldTrack(string symbol)
        {
            if (_cfg.IncludeSymbols.Length > 0)
            {
                foreach (var s in _cfg.IncludeSymbols)
                    if (symbol.Equals(s, StringComparison.OrdinalIgnoreCase))
                        return true;
                return false;
            }
            foreach (var s in _cfg.ExcludeSymbols)
                if (symbol.Equals(s, StringComparison.OrdinalIgnoreCase))
                    return false;
            return true;
        }
    }
}
