using Microsoft.EntityFrameworkCore;
using ObserverBot.App;
using ObserverBot.Services;
using ObserverBot.Utils;
using Serilog;
using Shared;
using Shared.Models;
using System.Globalization;
using System.Text.RegularExpressions;

namespace RecoMakerBot
{
    internal static class Program
    {
        private static async Task<int> Main(string[] args)
        {
            var options = new DbContextOptionsBuilder<SharedDbContext>()
                .UseSqlServer("Server=localhost;Database=Tradexus;Trusted_Connection=True;Encrypt=False;")
                .Options;

            EnvLoader.LoadEnvFile(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "poloniex.env"));

            Log.Logger = new LoggerConfiguration()
                .WriteTo.Console()
                .MinimumLevel.Verbose()
                .CreateLogger();

            Log.Warning("ObserverBot starting (monitor-only)…");

            var cts = new CancellationTokenSource();
            var cfg = AppConfig.Default();

            var metrics = new MetricsService();
            var heartbeat = new HeartbeatService(metrics, Log.Logger);
            heartbeat.Start(TimeSpan.FromSeconds(30));

            var ws = new PublicWebSocketService(Log.Logger, metrics);
            var symbols = new SymbolCatalogService(Log.Logger);
            var watch = new WatchlistService(cfg, Log.Logger);
            var price = new PriceMonitorService(Log.Logger, metrics);
            var mtf = new MtfAggregatorService(cfg.Timeframes, Log.Logger, metrics);
            var patterns = new PatternEngineService(Log.Logger, metrics);

            var backfillWindow = DateTime.UtcNow.AddHours(-120);


            using (var db = new SharedDbContext(options))
            {
                //"XRP_USDT", "BTC_USDT", "ETH_USDT", "ADA_USDT", "HBAR_USDT", "LINK_USDT"
                    //backfill the last few hours just in case
                    await price.BackfillRangeAsync(db, new[] { "XRP_USDT", "BTC_USDT", "ETH_USDT", "ADA_USDT", "HBAR_USDT", "LINK_USDT" }, new[] { TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5) }, startUtc: DateTime.UtcNow.AddMinutes(-120), endUtc: DateTime.UtcNow);

                Log.Information("[Prime] History loaded, enabling live recommendations");
            }

            // Flag for priming phase
            bool priming = true;


            // ---- PRIMING: seed 6 months of history into MTF for ALL available timeframes ----
            {
                var primeStart = DateTime.UtcNow.AddMonths(-6);
                var primeEnd = DateTime.UtcNow;

                Log.Information("[Prime] Starting priming from {Start:o} to {End:o}", primeStart, primeEnd);

                // Helper: parse timeframe strings like "1m", "5m", "1h", "1d", "3d", "7d", "30d"
                static bool TryParseTf(string s, out TimeSpan tf)
                {
                    tf = TimeSpan.Zero;
                    if (string.IsNullOrWhiteSpace(s)) return false;
                    var m = Regex.Match(s.Trim().ToLowerInvariant(), @"^\s*(\d+(?:\.\d+)?)\s*([mhd])\s*$");
                    if (!m.Success) return false;

                    var val = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                    tf = m.Groups[2].Value switch
                    {
                        "m" => TimeSpan.FromMinutes(val),
                        "h" => TimeSpan.FromHours(val),
                        "d" => TimeSpan.FromDays(val),
                        _ => TimeSpan.Zero
                    };
                    return tf > TimeSpan.Zero;
                }

                // Helper: capacity to keep full 6 months per timeframe (guarded with an upper bound)
                int ComputeHistoryCap(TimeSpan tf)
                {
                    if (tf <= TimeSpan.Zero) return 1500;
                    var totalBars = (int)Math.Ceiling((primeEnd - primeStart).TotalMinutes / tf.TotalMinutes);
                    return Math.Min(Math.Max(totalBars, 1500), 500_000); // cap to avoid runaway memory
                }

                HashSet<TimeSpan> configuredTfs = new HashSet<TimeSpan>(cfg.Timeframes);
                List<(TimeSpan tf, string tfStr)> tfsToPrime;

                // Discover available TFs from the DB (within prime window), intersect with configured TFs
                using (var db = new SharedDbContext(options))
                {
                    var tfStrings = db.Candles.AsNoTracking()
                        .Where(c => c.StartTime >= primeStart && c.StartTime < primeEnd)
                        .Select(c => c.Timeframe)
                        .Distinct()
                        .ToList();

                    var parsed = new List<(TimeSpan tf, string tfStr)>();
                    foreach (var s in tfStrings)
                        if (TryParseTf(s, out var tf))
                            parsed.Add((tf, s));

                    tfsToPrime = parsed
                        .Where(x => configuredTfs.Contains(x.tf))
                        .OrderBy(x => x.tf)
                        .ToList();
                }

                if (tfsToPrime.Count == 0)
                {
                    Log.Warning("[Prime] No overlapping timeframes between DB and configuration. Skipping.");
                }
                else
                {
                    Log.Information("[Prime] Will prime {TfCount} timeframe(s): {Tfs}",
                        tfsToPrime.Count, string.Join(", ", tfsToPrime.Select(x => x.tfStr)));

                    foreach (var (tf, tfStr) in tfsToPrime)
                    {
                        // Determine symbols to prime for this timeframe
                        List<string> symbolsToPrime;
                        using (var db = new SharedDbContext(options))
                        {
                            if (cfg.IncludeSymbols != null && cfg.IncludeSymbols.Length > 0)
                            {
                                var includeSet = new HashSet<string>(cfg.IncludeSymbols, StringComparer.OrdinalIgnoreCase);
                                // Only keep symbols that actually have candles in this TF + window
                                symbolsToPrime = db.Candles.AsNoTracking()
                                    .Where(c => c.Timeframe == tfStr && c.StartTime >= primeStart && c.StartTime < primeEnd && includeSet.Contains(c.Symbol))
                                    .Select(c => c.Symbol)
                                    .Distinct()
                                    .ToList();
                            }
                            else
                            {
                                symbolsToPrime = db.Candles.AsNoTracking()
                                    .Where(c => c.Timeframe == tfStr && c.StartTime >= primeStart && c.StartTime < primeEnd)
                                    .Select(c => c.Symbol)
                                    .Distinct()
                                    .ToList();
                            }
                        }

                        if (symbolsToPrime.Count == 0)
                        {
                            Log.Information("[Prime] {Tf}: no symbols present in DB for window.", tfStr);
                            continue;
                        }

                        var cap = ComputeHistoryCap(tf);
                        var window = tf < TimeSpan.FromHours(1) ? TimeSpan.FromDays(7) : TimeSpan.FromDays(30);

                        Log.Information("[Prime] {Tf}: priming {SymCount} symbol(s) with cap={Cap}.", tfStr, symbolsToPrime.Count, cap);

                        foreach (var sym in symbolsToPrime)
                        {
                            if (!watch.ShouldTrack(sym))
                                continue;

                            Log.Information("[Prime] {Tf} {Sym}: loading history…", tfStr, sym);
                            var windowStart = primeStart;
                            var firstChunk = true;
                            var loaded = 0;

                            while (windowStart < primeEnd)
                            {
                                var windowEnd = windowStart + window;
                                if (windowEnd > primeEnd) windowEnd = primeEnd;

                                // Pull a sorted slice of candles and map to TickerBar
                                List<TickerBar> bars;
                                using (var db = new SharedDbContext(options))
                                {
                                    bars = db.Candles.AsNoTracking()
                                        .Where(c =>
                                            c.Symbol == sym &&
                                            c.Timeframe == tfStr &&
                                            c.StartTime >= windowStart &&
                                            c.StartTime < windowEnd)
                                        .OrderBy(c => c.StartTime)
                                        .Select(c => new TickerBar
                                        {
                                            Symbol = c.Symbol,
                                            MinuteUtc = DateTime.SpecifyKind(c.StartTime, DateTimeKind.Utc),
                                            Open = c.Open,
                                            High = c.High,
                                            Low = c.Low,
                                            Close = c.Close,
                                            Volume = (decimal)c.Volume,
                                            Vwap60s = (decimal)c.Vwap60s
                                        })
                                        .ToList();
                                }

                                if (bars.Count > 0)
                                {
                                    // First chunk establishes the TfState with the desired capacity.
                                    if (firstChunk)
                                    {
                                        mtf.PrimeHistory(tf, sym, bars, historyLimit: cap);
                                        firstChunk = false;
                                    }
                                    else
                                    {
                                        // Subsequent chunks append; capacity remains as set on first call.
                                        mtf.PrimeHistory(tf, sym, bars);
                                    }

                                    loaded += bars.Count;
                                    if (loaded % 10000 == 0)
                                        Log.Information("[Prime] {Tf} {Sym}: replayed {Count} bars so far…", tfStr, sym, loaded);
                                }

                                windowStart = windowEnd;
                            }

                            Log.Information("[Prime] {Tf} {Sym}: replay complete, {Count} bars.", tfStr, sym, loaded);
                        }
                    }
                }
            }
            // ---- END PRIMING ----


            // ---- END PRIMING ----


            // End priming
            Log.Information("[Prime] History loaded, enabling live recommendations");
            priming = false;


            //MISSING PRIMING LOGICS


            // End priming
            Log.Information("[Prime] History loaded, enabling live recommendations");
            priming = false;

            // WebSocket events
            ws.OnConnected += () => Log.Information("[WS] connected");
            ws.OnDisconnected += (code, reason) => Log.Warning("[WS] disconnected {Code} {Reason}", code, reason);
            ws.OnError += ex => Log.Error(ex, "[WS] error");

            ws.OnSymbols += (action, arr) =>
            {
                var (updated, total) = symbols.ApplyUpdate(action, arr);
                Log.Information("[Symbols] {Action} updated={Updated}/{Total}", action, updated, total);
            };

            ws.OnTicker += e =>
            {
                var sym = e.GetProperty("symbol").GetString() ?? "";
                if (!watch.ShouldTrack(sym) || !symbols.IsTrading(sym)) return;
                price.OnTicker(e);
            };

            // Price events
            price.OnBarClosed += (TickerBar bar) =>
            {
                Log.Information("[BarClose] {Sym} {Ts:o} O:{O} H:{H} L:{L} C:{C} VWAP60:{V}",
                    bar.Symbol, bar.MinuteUtc, bar.Open, bar.High, bar.Low, bar.Close, bar.Vwap60s);

                using (var db = new SharedDbContext(options))
                {
                    if (!db.Candles.Any(x => x.Symbol == bar.Symbol && x.Timeframe == "1m" && x.StartTime == bar.MinuteUtc))
                    {
                        db.Candles.Add(new Candle
                        {
                            Symbol = bar.Symbol,
                            Timeframe = "1m",
                            StartTime = bar.MinuteUtc,
                            Open = bar.Open,
                            High = bar.High,
                            Low = bar.Low,
                            Close = bar.Close,
                            Volume = bar.Volume,
                            Vwap60s = bar.Vwap60s
                        });
                        db.SaveChanges();
                    }
                }

                mtf.OnOneMinuteBar(bar);
            };

            price.OnSignal += s =>
            {
                Log.Information("[Signal] {Mode} {Symbol} note={Note}", s.Mode, s.Symbol, s.Note);
            };

            // MTF events
            mtf.OnTfBarClosed += (tf, bar) =>
            {
                var tfStr = FormatTf(tf);

                using (var db = new SharedDbContext(options))
                {
                    if (!db.Candles.Any(x => x.Symbol == bar.Symbol && x.Timeframe == tfStr && x.StartTime == bar.MinuteUtc))
                    {
                        db.Candles.Add(new Candle
                        {
                            Symbol = bar.Symbol,
                            Timeframe = tfStr,
                            StartTime = bar.MinuteUtc,
                            Open = bar.Open,
                            High = bar.High,
                            Low = bar.Low,
                            Close = bar.Close,
                            Volume = bar.Volume,
                            Vwap60s = bar.Vwap60s
                        });
                        db.SaveChanges();
                    }
                }

                Log.Debug("[TFBar] {Sym} {Tf} C:{C}", bar.Symbol, FormatTf(tf), bar.Close);
            };

            mtf.OnTfHistory += (tf, symbol, hist) =>
            {
                if (!priming) // only emit recos when live
                {
                    foreach (var reco in PatternEngineService.DefaultEngine.OnBarClosed(symbol, tf, hist))
                    {
                        patterns.Emit(reco);
                    }
                }
            };

            patterns.OnRecommendation += r =>
            {
                Log.Information("[Reco][{Tf}] {Pattern} {Sym} dir={Dir} conf={Conf:F2} px={Px} {Note}", FormatTf(r.Timeframe), r.Pattern, r.Symbol, r.Direction, r.Confidence, r.RefPrice, r.Note ?? "");
                try
                {
                    using (var db = new SharedDbContext(options))
                    {
                        var log = new RecoLog
                        {
                            Symbol = r.Symbol,
                            Timeframe = FormatTf(r.Timeframe),
                            Timestamp = DateTime.UtcNow,
                            Detector = r.Pattern,
                            Direction = r.Direction,
                            Confidence = r.Confidence,
                            Price = r.RefPrice,
                            Context = r.Note
                        };

                        db.RecoLog.Add(log);
                        db.SaveChanges();
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to insert reco log");
                }
            };

            await ws.RunAsync(cts.Token);
            return 0;
        }

        private static string FormatTf(TimeSpan tf)
        {
            if (tf.TotalDays >= 1) return $"{tf.TotalDays:0.#}d";
            if (tf.TotalHours >= 1) return $"{tf.TotalHours:0.#}h";
            return $"{tf.TotalMinutes:0.#}m";
        }
    }
}