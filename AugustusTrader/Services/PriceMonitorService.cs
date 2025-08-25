using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Shared;
using Shared.Models;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;

namespace ObserverBot.Services
{
    public sealed class PriceMonitorService : IDisposable
    {
        private readonly ILogger _log;
        private readonly MetricsService _metrics;
        private readonly System.Threading.Timer _timer;
        public Action<TickerBar>? OnBarClosed;
        public Action<Signal>? OnSignal;

        private readonly ConcurrentDictionary<string, SymbolState> _state = new(StringComparer.OrdinalIgnoreCase);

        public PriceMonitorService(ILogger log, MetricsService metrics)
        {
            _log = log;
            _metrics = metrics;
            _timer = new System.Threading.Timer(_ => { }, null, 1000, 1000);
        }

        public void OnTicker(JsonElement e)
        {
            var symbol = e.GetProperty("symbol").GetString() ?? "";
            var closeS = e.TryGetProperty("close", out var c) ? (c.GetString() ?? "0") : "0";
            var openS = e.TryGetProperty("open", out var o) ? (o.GetString() ?? "0") : "0";
            var highS = e.TryGetProperty("high", out var h) ? (h.GetString() ?? "0") : "0";
            var lowS = e.TryGetProperty("low", out var l) ? (l.GetString() ?? "0") : "0";
            var tsMs = e.TryGetProperty("ts", out var t) ? t.GetInt64() : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            if (!decimal.TryParse(closeS, NumberStyles.Any, CultureInfo.InvariantCulture, out var close)) return;
            decimal.TryParse(openS, NumberStyles.Any, CultureInfo.InvariantCulture, out var open);
            decimal.TryParse(highS, NumberStyles.Any, CultureInfo.InvariantCulture, out var high);
            decimal.TryParse(lowS, NumberStyles.Any, CultureInfo.InvariantCulture, out var low);

            var ts = DateTimeOffset.FromUnixTimeMilliseconds(tsMs).UtcDateTime;

            var st = _state.GetOrAdd(symbol, _ => new SymbolState(symbol));

            var barStart = new DateTime(ts.Year, ts.Month, ts.Day, ts.Hour, ts.Minute, 0, DateTimeKind.Utc);
            if (st.BarStart != barStart)
            {
                if (st.BarStart != default)
                {
                    var closedBar = new TickerBar
                    {
                        Symbol = st.Symbol,
                        MinuteUtc = st.BarStart,
                        Open = st.Open,
                        High = st.High,
                        Low = st.Low,
                        Close = st.Close,
                        Volume = st.Volume,
                        Vwap60s = st.Vwap60s
                    };
                    OnBarClosed?.Invoke(closedBar);
                }

                st.BarStart = barStart;
                st.Open = close;
                st.High = close;
                st.Low = close;
                st.Close = close;
                st.Volume = 0m;
            }
            else
            {
                if (st.Open == 0) st.Open = close;
                if (close > st.High) st.High = close;
                if (close < st.Low) st.Low = close;
                st.Close = close;
            }

            st.LastClose = close;
            st.LastTs = ts;

            st.Rolling.AddSample(ts, close, 1m);
            st.Vwap60s = st.Rolling.Vwap(TimeSpan.FromSeconds(60));

            if (st.Vwap60s > 0 && (close - st.Vwap60s) / st.Vwap60s >= 0.005m && st.Rolling.CountWithin(TimeSpan.FromSeconds(60)) >= 10)
            {
                OnSignal?.Invoke(new Signal
                {
                    Symbol = symbol,
                    TimeUtc = ts,
                    Mode = "Momentum",
                    Note = "Close > VWAP60s by 0.5%",
                    ParamsJson = "{\"tpPct\":0.30,\"slPct\":0.10}"
                });
            }
        }

        public void Dispose() => _timer.Dispose();

        private sealed class SymbolState
        {
            public string Symbol { get; }
            public DateTime BarStart;
            public decimal Open, High, Low, Close, Volume;
            public decimal LastClose;
            public DateTime LastTs;
            public decimal Vwap60s;
            public RollingWindow Rolling = new();
            public SymbolState(string symbol) { Symbol = symbol; }
        }

        private sealed class RollingWindow
        {
            private readonly System.Collections.Generic.LinkedList<(DateTime ts, decimal px, decimal w)> _q = new();
            public void AddSample(DateTime ts, decimal price, decimal weight)
            {
                _q.AddLast((ts, price, weight));
                Prune(ts, TimeSpan.FromSeconds(60));
            }
            public int CountWithin(TimeSpan span)
            {
                var now = _q.Last?.Value.ts ?? DateTime.UtcNow;
                Prune(now, span);
                return _q.Count;
            }
            public decimal Vwap(TimeSpan span)
            {
                if (_q.Count == 0) return 0;
                var now = _q.Last.Value.ts;
                Prune(now, span);
                decimal pv = 0, w = 0;
                foreach (var (ts, px, wt) in _q)
                {
                    pv += px * wt;
                    w += wt;
                }
                return w == 0 ? 0 : pv / w;
            }
            private void Prune(DateTime now, TimeSpan span)
            {
                while (_q.First is not null && now - _q.First.Value.ts > span)
                    _q.RemoveFirst();
            }
        }

        public sealed class Signal
        {
            public string Symbol { get; set; } = "";
            public DateTime TimeUtc { get; set; }
            public string Mode { get; set; } = "";
            public string Note { get; set; } = "";
            public string? ParamsJson { get; set; }
        }

        private readonly HttpClient _http = new();

public async Task BackfillRangeAsync(
    SharedDbContext db,
    IEnumerable<string> symbols,
    IEnumerable<TimeSpan> timeframes,
    DateTime startUtc,
    DateTime endUtc,
    int apiLimitBars = 100,
    CancellationToken ct = default)
    {
        using var http = new HttpClient();

        foreach (var sym in symbols)
        {
            foreach (var tf in timeframes)
            {
                var tfCode = MapTf(tf);         // e.g., "MINUTE_1", "DAY_1"
                var tfStr = FormatTf(tf);      // e.g., "1m", "1d"
                if (tfCode == null)
                {
                    Console.WriteLine($"[Backfill] Skipping unsupported TF {tf}");
                    continue;
                }

                // Choose a reasonable outer window:
                // - For sub-daily TFs, use 1 day windows (lets us cache "have" set per day).
                // - For daily-or-higher TFs, use larger windows (90 days) to go faster.
                var outerWindow = (tf < TimeSpan.FromDays(1)) ? TimeSpan.FromDays(1) : TimeSpan.FromDays(90);

                // Align windowStart to midnight UTC for sub-daily TFs; otherwise keep startUtc as-is.
                DateTime windowStart = (tf < TimeSpan.FromDays(1))
                    ? new DateTime(startUtc.Year, startUtc.Month, startUtc.Day, 0, 0, 0, DateTimeKind.Utc)
                    : startUtc;

                if (windowStart < startUtc) windowStart = startUtc;

                Console.WriteLine($"[Backfill] {sym} {tfStr} from {startUtc:u} to {endUtc:u}");

                while (windowStart < endUtc)
                {
                    var windowEnd = windowStart + outerWindow;
                    if (windowEnd > endUtc) windowEnd = endUtc;

                    // Prefetch existing times for this window (dedupe cache).
                    var existingTimes = await db.Candles
                        .Where(x => x.Symbol == sym && x.Timeframe == tfStr &&
                                    x.StartTime >= windowStart && x.StartTime < windowEnd)
                        .Select(x => x.StartTime)                        
                        .ToListAsync(ct);

                    var have = new HashSet<DateTime>(existingTimes);

                    // Each request should be <= apiLimitBars wide:
                    var segSpan = TimeSpan.FromTicks(tf.Ticks * apiLimitBars);
                    var segStart = windowStart;

                    while (segStart < windowEnd)
                    {
                        var segEnd = segStart + segSpan;
                        if (segEnd > windowEnd) segEnd = windowEnd;

                        var url =
                            $"https://api.poloniex.com/markets/{sym}/candles" +
                            $"?interval={tfCode}" +
                            $"&startTime={ToUnixMs(segStart)}" +
                            $"&endTime={ToUnixMs(segEnd)}" +
                            $"&limit={apiLimitBars}";

                        List<List<JsonElement>>? raw = null;

                        // Simple retry/backoff
                        for (int attempt = 1; attempt <= 3; attempt++)
                        {
                            try
                            {
                                using var resp = await http.GetAsync(url, ct);
                                if (!resp.IsSuccessStatusCode)
                                {
                                    await Task.Delay(250 * attempt, ct);
                                    continue;
                                }
                                var json = await resp.Content.ReadAsStringAsync(ct);
                                raw = JsonSerializer.Deserialize<List<List<JsonElement>>>(json);
                                break;
                            }
                            catch
                            {
                                await Task.Delay(300 * attempt, ct);
                            }
                        }

                        if (raw == null)
                        {
                            Console.WriteLine($"[Backfill] {sym} {tfStr} {segStart:u}->{segEnd:u} (no data / failed)");
                            segStart = segEnd; // avoid infinite loop
                            continue;
                        }

                        var toInsert = new List<Candle>(raw.Count);
                        DateTime? lastTs = null;

                        foreach (var row in raw)
                        {
                            // Defensive parse. Your prior mapping used:
                            // [0]=open, [1]=high, [2]=low, [3]=close, [4]=volume, [9]=timestamp(ms), [10]=vwap(string)
                            if (row.Count < 11) continue;

                            var ts = DateTimeOffset.FromUnixTimeMilliseconds(row[9].GetInt64()).UtcDateTime;
                            if (lastTs == null || ts > lastTs) lastTs = ts;

                            // skip if already present in this window cache
                            if (have.Contains(ts)) continue;

                            if (!TryDec(row[0], out var open)) continue;
                            if (!TryDec(row[1], out var high)) continue;
                            if (!TryDec(row[2], out var low)) continue;
                            if (!TryDec(row[3], out var close)) continue;

                            // volume is optional; treat missing as 0
                            TryDec(row[4], out var volume);

                            decimal? vwap = null;
                            if (row[10].ValueKind == JsonValueKind.String &&
                                decimal.TryParse(row[10].GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var vw))
                            {
                                vwap = vw;
                            }

                            toInsert.Add(new Candle
                            {
                                Symbol = sym,
                                Timeframe = tfStr,
                                StartTime = ts,
                                Open = open,
                                High = high,
                                Low = low,
                                Close = close,
                                Volume = volume,
                                Vwap60s = vwap
                            });
                        }

                        // De-dup the batch itself (in case API returns duplicate timestamps)
                        if (toInsert.Count > 1)
                        {
                            toInsert = toInsert
                                .GroupBy(c => c.StartTime)
                                .Select(g => g.First())
                                .ToList();
                        }

                        // Final guard against rows that slipped in between prefetch and save (race/re-run)
                        if (toInsert.Count > 0)
                        {
                            var tsList = toInsert.Select(c => c.StartTime).ToList();
                            var alreadyThere = await db.Candles
                                .Where(x => x.Symbol == sym && x.Timeframe == tfStr && tsList.Contains(x.StartTime))
                                .Select(x => x.StartTime)
                                .ToListAsync(ct);

                            if (alreadyThere.Count > 0)
                            {
                                var alreadySet = alreadyThere.ToHashSet();
                                toInsert.RemoveAll(c => alreadySet.Contains(c.StartTime));
                            }
                        }

                        if (toInsert.Count > 0)
                        {
                            try
                            {
                                db.Candles.AddRange(toInsert);
                                await db.SaveChangesAsync(ct);

                                // Update cache so later segments in the same window won't re-add
                                foreach (var c in toInsert) have.Add(c.StartTime);

                                Console.WriteLine($"[Backfill] (+{toInsert.Count}) {sym} {tfStr} {segStart:u}->{segEnd:u}");
                            }
                            catch (DbUpdateException ex) when (ex.InnerException is SqlException sql &&
                                                               (sql.Number == 2601 || sql.Number == 2627)) // duplicate key
                            {
                                // Filter dupes and retry once
                                var tsList = toInsert.Select(c => c.StartTime).ToList();
                                var dups = await db.Candles
                                    .Where(x => x.Symbol == sym && x.Timeframe == tfStr && tsList.Contains(x.StartTime))
                                    .Select(x => x.StartTime)
                                    .ToListAsync(ct);

                                if (dups.Count > 0)
                                {
                                    var dupSet = dups.ToHashSet();
                                    toInsert.RemoveAll(c => dupSet.Contains(c.StartTime));
                                }

                                if (toInsert.Count > 0)
                                {
                                    db.Candles.AddRange(toInsert);
                                    await db.SaveChangesAsync(ct);

                                    foreach (var c in toInsert) have.Add(c.StartTime);
                                    Console.WriteLine($"[Backfill] (+{toInsert.Count}) {sym} {tfStr} {segStart:u}->{segEnd:u} (after de-dupe)");
                                }
                                else
                                {
                                    Console.WriteLine($"[Backfill] (+0) {sym} {tfStr} {segStart:u}->{segEnd:u} (all dupes)");
                                }
                            }
                        }
                        else
                        {
                            Console.WriteLine($"[Backfill] (+0) {sym} {tfStr} {segStart:u}->{segEnd:u}");
                        }

                        // Advance by “last returned ts + one bar” (or segEnd if nothing came back)
                        segStart = (lastTs ?? segEnd) + tf;

                        // Gentle pacing
                        await Task.Delay(120, ct);
                    }

                    windowStart = windowEnd;
                }
            }
        }

        // ---- helpers ----
        static bool TryDec(JsonElement e, out decimal v)
        {
            v = 0m;
            if (e.ValueKind == JsonValueKind.Number) return e.TryGetDecimal(out v);
            if (e.ValueKind == JsonValueKind.String)
                return decimal.TryParse(e.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out v);
            return false;
        }

        static long ToUnixMs(DateTime dtUtc) =>
            new DateTimeOffset(DateTime.SpecifyKind(dtUtc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();

        static string? MapTf(TimeSpan tf) =>
            //tf == TimeSpan.FromMinutes(1) ? "MINUTE_1" :
            tf == TimeSpan.FromMinutes(5) ? "MINUTE_5" :
            tf == TimeSpan.FromMinutes(15) ? "MINUTE_15" :
            tf == TimeSpan.FromMinutes(30) ? "MINUTE_30" :
            tf == TimeSpan.FromHours(1) ? "HOUR_1" :
            tf == TimeSpan.FromHours(2) ? "HOUR_2" :
            tf == TimeSpan.FromHours(6) ? "HOUR_6" :
            tf == TimeSpan.FromHours(12) ? "HOUR_12" :
            tf == TimeSpan.FromDays(1) ? "DAY_1" :
            tf == TimeSpan.FromDays(3) ? "DAY_3" :
            tf == TimeSpan.FromDays(7) ? "DAY_7" :
            tf == TimeSpan.FromDays(30) ? "DAY_30" : null;

        static string FormatTf(TimeSpan tf) =>
            (tf.TotalMinutes < 60) ? $"{(int)tf.TotalMinutes}m" :
            (tf.TotalHours < 24) ? $"{(int)tf.TotalHours}h" :
                                     $"{(int)tf.TotalDays}d";
    }

    


    private string MapTf(TimeSpan tf) => tf switch
        {
            { TotalMinutes: 1 } => "MINUTE_1",
            { TotalMinutes: 5 } => "MINUTE_5",
            { TotalMinutes: 10 } => "MINUTE_10",
            { TotalMinutes: 15 } => "MINUTE_15",
            { TotalMinutes: 30 } => "MINUTE_30",

            { TotalHours: 1 } => "HOUR_1",
            { TotalHours: 2 } => "HOUR_2",
            { TotalHours: 4 } => "HOUR_4",
            { TotalHours: 6 } => "HOUR_6",
            { TotalHours: 12 } => "HOUR_12",

            { TotalDays: 1 } => "DAY_1",
            { TotalDays: 3 } => "DAY_3",
            { TotalDays: 7 } => "WEEK_1",
            { TotalDays: 30 } => "MONTH_1",
            _ => null // unsupported, we’ll skip
        };

        private string FormatTf(TimeSpan tf) =>
            tf.TotalDays >= 1 ? $"{tf.TotalDays:0.#}d" :
            tf.TotalHours >= 1 ? $"{tf.TotalHours:0.#}h" :
            $"{tf.TotalMinutes:0.#}m";

        private class RestCandle
        {
            public DateTime StartTime { get; set; }
            public decimal Open { get; set; }
            public decimal High { get; set; }
            public decimal Low { get; set; }
            public decimal Close { get; set; }
            public decimal Volume { get; set; }
        }
    }
}
