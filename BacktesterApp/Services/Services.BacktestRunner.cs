using BacktesterApp.Engine;
using BacktesterStandalone.Engine;
using EFCore.BulkExtensions;
using Microsoft.EntityFrameworkCore;
using Shared;

namespace BacktesterApp.Services
{
    public class BacktestRunner
    {
        private readonly SharedDbContext _src;
        private readonly SharedDbContext _dest;
        private readonly PatternEngine _engine;

        public BacktestRunner(SharedDbContext src, SharedDbContext dest)
        {
            _src = src;
            _dest = dest;
            _engine = PatternEngine.Default();
        }

        public async Task RunAsync(
            string symbol,
            string timeframe,
            DateTime start,
            DateTime end,
            int insertBatch = 10000)
        {
            var tf = ParseTimeframe(timeframe);

            var hist = new List<TickerBarBacktest>(2048);
            var buffer = new List<RecoLog>(insertBatch);

            var query = _src.Candles
                .Where(c => c.Symbol == symbol && c.Timeframe == timeframe &&
                            c.StartTime >= start && c.StartTime <= end)
                .OrderBy(c => c.StartTime)
                .AsNoTracking();

            await foreach (var c in query.AsAsyncEnumerable())
            {
                var bar = new TickerBarBacktest
                {
                    StartTime = c.StartTime,
                    Open = c.Open,
                    High = c.High,
                    Low = c.Low,
                    Close = c.Close,
                    Volume = c.Volume ?? 0m,
                    Vwap60s = c.Vwap60s ?? 0m
                };
                hist.Add(bar);

                // evaluate patterns on current history
                foreach (var r in _engine.EvaluateAll(symbol, tf, hist))
                {
                    buffer.Add(new RecoLog
                    {
                        Symbol = r.Symbol,
                        Timeframe = timeframe,
                        Timestamp = c.StartTime,
                        Detector = r.Pattern,
                        Direction = r.Direction,
                        Confidence = r.Confidence,
                        Price = r.RefPrice,
                        Context = r.Note
                    });
                }

                if (buffer.Count >= insertBatch)
                {
                    await _dest.BulkInsertAsync(buffer);
                    buffer.Clear();
                }
            }

            if (buffer.Count > 0)
            {
                await _dest.BulkInsertAsync(buffer);
                buffer.Clear();
            }
        }

        private static TimeSpan ParseTimeframe(string tf)
        {
            if (tf.EndsWith("m")) return TimeSpan.FromMinutes(int.Parse(tf[..^1]));
            if (tf.EndsWith("h")) return TimeSpan.FromHours(int.Parse(tf[..^1]));
            if (tf.EndsWith("d")) return TimeSpan.FromDays(int.Parse(tf[..^1]));
            return TimeSpan.FromMinutes(1); // fallback
        }
    }
}
