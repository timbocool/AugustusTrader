using EFCore.BulkExtensions;
using Microsoft.EntityFrameworkCore;
using Shared;
using Shared.Models;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BacktesterApp.Services
{
    public class BacktestScoringWorker
    {
        private readonly DbContextOptions<SharedDbContext> _srcOptions;
        private readonly DbContextOptions<SharedDbContext> _destOptions;
        private readonly BacktestScoringParams _p;
        private readonly string _configHash;
        public string ConfigHash => _configHash;


        public BacktestScoringWorker(
            DbContextOptions<SharedDbContext> srcOptions,
            DbContextOptions<SharedDbContext> destOptions,
            BacktestScoringParams parameters)
        {
            _srcOptions = srcOptions;
            _destOptions = destOptions;
            _p = parameters;
            _configHash = ComputeConfigHash(parameters);
        }

        // === Public orchestrator ===
        public async Task ScoreAllPendingAsync(int batchSize = 5000, int maxParallel = 4)
        {
            var cutoff = DateTime.UtcNow - _p.MinAge;

            bool hasMore;
            do
            {
                using var destFetch = new SharedDbContext(_destOptions);

                var candidates = await destFetch.RecoLog
                    .Where(r => r.Timestamp <= cutoff)
                    .Where(r => !destFetch.RecoEvaluation
                        .Any(e => e.RecoId == r.Id && e.ConfigHash == _configHash))
                    .OrderBy(r => r.Symbol).ThenBy(r => r.Timeframe).ThenBy(r => r.Timestamp)
                    .Take(batchSize)
                    .AsNoTracking()
                    .ToListAsync();

                hasMore = candidates.Count == batchSize;

                if (candidates.Count == 0)
                {
                    Console.WriteLine("No more recos to score.");
                    return;
                }

                var groups = candidates.GroupBy(r => new { r.Symbol, r.Timeframe });
                using var semaphore = new SemaphoreSlim(maxParallel);

                var tasks = groups.Select(async grp =>
                {
                    await semaphore.WaitAsync();
                    try { await ScoreGroupAsync(grp.Key.Symbol, grp.Key.Timeframe, grp.ToList()); }
                    finally { semaphore.Release(); }
                });

                await Task.WhenAll(tasks);

            } while (hasMore);
        }

        // === Per (symbol, timeframe) group ===
        private async Task ScoreGroupAsync(string sym, string tf, List<RecoLog> group)
        {
            var minTs = group.Min(r => r.Timestamp);
            var maxTs = group.Max(r => r.Timestamp);

            var tfSpan = ParseTimeframe(tf); // throws if unknown
            var horizonSpan = TimeSpan.FromTicks(tfSpan.Ticks * _p.HorizonBars);

            using var src = new SharedDbContext(_srcOptions);
            using var dest = new SharedDbContext(_destOptions);

            // Load enough candles for entry lag and horizon
            var start = minTs - tfSpan * _p.CandlePadBarsBefore;
            var end = maxTs + tfSpan * (_p.EntryLagBars + _p.HorizonBars + _p.CandlePadBarsAfter);

            var candles = await src.Candles
                .Where(c => c.Symbol == sym && c.Timeframe == tf &&
                            c.StartTime >= start && c.StartTime <= end)
                .OrderBy(c => c.StartTime)
                .AsNoTracking()
                .ToListAsync();

            if (candles.Count == 0) return;

            // Fast timestamp lookup
            var starts = candles.Select(c => c.StartTime).ToArray();

            int IndexAtOrAfter(DateTime ts)
            {
                int lo = 0, hi = starts.Length - 1, ans = starts.Length;
                while (lo <= hi)
                {
                    int mid = lo + hi >> 1;
                    if (starts[mid] >= ts) { ans = mid; hi = mid - 1; }
                    else lo = mid + 1;
                }
                return ans < starts.Length ? ans : -1;
            }

            var evalBuffer = new List<RecoEvaluation>(group.Count);

            foreach (var r in group)
            {
                // Signal bar index
                var sigIdx = IndexAtOrAfter(r.Timestamp);
                if (sigIdx < 0) continue;

                // Entry on next bar by default (EntryLagBars)
                var entryIdx = sigIdx + _p.EntryLagBars;
                if (entryIdx < 0 || entryIdx >= candles.Count) continue;

                var exitIdx = entryIdx + _p.HorizonBars;
                if (exitIdx >= candles.Count) continue;

                // Optional contiguity check
                bool hasGap = _p.RequireContiguousData && HasGap(candles, entryIdx, exitIdx, tfSpan);
                if (hasGap)
                {
                    evalBuffer.Add(new RecoEvaluation
                    {
                        RecoId = r.Id,
                        ConfigHash = _configHash,
                        HorizonBars = _p.HorizonBars,
                        EvaluatedAt = DateTime.UtcNow,
                        EntryBarStartTime = candles[entryIdx].StartTime,
                        Outcome = "GAP",
                        OutcomeDetail = "missing-candles",
                        Notes = JsonSerializer.Serialize(new { r.Id, sym, tf, gap = true, p = _p })
                    });
                    continue;
                }

                var entryBar = candles[entryIdx];
                var endBar = candles[exitIdx];

                decimal entryPx = _p.EntryPrice switch
                {
                    EntryPriceMode.Open => entryBar.Open,
                    EntryPriceMode.Close => entryBar.Close,
                    EntryPriceMode.Vwap60s => entryBar.Vwap60s ?? entryBar.Open,
                    _ => entryBar.Open
                };

                if (entryPx == 0) continue;
                var denom = entryPx;

                // Targets (if configured)
                decimal? targetPx = null, stopPx = null;
                if (_p.TargetPct is not null)
                    targetPx = r.Direction == "BUY"
                        ? entryPx * (1 + _p.TargetPct.Value)
                        : entryPx * (1 - _p.TargetPct.Value);

                if (_p.StopPct is not null)
                    stopPx = r.Direction == "BUY"
                        ? entryPx * (1 - _p.StopPct.Value)
                        : entryPx * (1 + _p.StopPct.Value);

                // Walk the path if Stop/Target mode
                int lastIdx = exitIdx;
                string outcome = "HORIZON";
                string outcomeDetail = "horizon-close";
                int ambiguousBars = 0;

                if (_p.Exit == ExitMode.StopOrTargetElseHorizon && (targetPx is not null || stopPx is not null))
                {
                    for (int i = entryIdx; i <= exitIdx; i++)
                    {
                        var b = candles[i];
                        bool hitTarget = false, hitStop = false;

                        if (r.Direction == "BUY")
                        {
                            if (targetPx is not null && b.High >= targetPx) hitTarget = true;
                            if (stopPx is not null && b.Low <= stopPx) hitStop = true;
                        }
                        else // SELL
                        {
                            if (targetPx is not null && b.Low <= targetPx) hitTarget = true;
                            if (stopPx is not null && b.High >= stopPx) hitStop = true;
                        }

                        if (hitTarget && hitStop)
                        {
                            ambiguousBars++;
                            switch (_p.TieBreak)
                            {
                                case TieBreakRule.StopFirst:
                                    outcome = "STOP"; outcomeDetail = "tie-stop-first"; lastIdx = i; goto ExitWalk;
                                case TieBreakRule.TargetFirst:
                                    outcome = "TARGET"; outcomeDetail = "tie-target-first"; lastIdx = i; goto ExitWalk;
                                case TieBreakRule.FavorProfit:
                                    // Favor the profitable side for the given direction
                                    if (r.Direction == "BUY") { outcome = "TARGET"; outcomeDetail = "tie-favor-profit"; }
                                    else { outcome = "TARGET"; outcomeDetail = "tie-favor-profit"; }
                                    lastIdx = i; goto ExitWalk;
                                case TieBreakRule.MarkAmbiguous:
                                    outcome = "AMBIG"; outcomeDetail = "target-and-stop-same-bar"; lastIdx = i; goto ExitWalk;
                            }
                        }
                        else if (hitTarget)
                        {
                            outcome = "TARGET"; outcomeDetail = "path-target"; lastIdx = i; goto ExitWalk;
                        }
                        else if (hitStop)
                        {
                            outcome = "STOP"; outcomeDetail = "path-stop"; lastIdx = i; goto ExitWalk;
                        }
                    }
                }
            ExitWalk:;

                // Exit price for P&L (conservative: use bar close where event resolved)
                var exitBar = candles[lastIdx];
                decimal exitPx =
                    outcome switch
                    {
                        "TARGET" or "STOP" => exitBar.Close, // bar resolved; use close (or make this configurable)
                        _ => _p.EntryPrice == EntryPriceMode.Close && _p.EntryLagBars == _p.HorizonBars
                                ? exitBar.Close
                                : exitBar.Close
                    };

                var grossRet = (exitPx - entryPx) / denom;
                if (r.Direction == "SELL") grossRet = -grossRet;

                // MFE/MAE (direction-aware) over [entryIdx..lastIdx]
                (decimal mfe, decimal mae) = ComputeMfeMae(candles, entryIdx, lastIdx, entryPx, r.Direction);

                // Net after costs
                var roundTripBps = 2m * (_p.FeeBpsPerSide + _p.SlippageBpsPerSide);
                var netRet = grossRet - roundTripBps / 10000m;

                var eval = new RecoEvaluation
                {
                    RecoId = r.Id,
                    ConfigHash = _configHash,
                    HorizonBars = _p.HorizonBars,
                    EvaluatedAt = DateTime.UtcNow,

                    EntryBarStartTime = entryBar.StartTime,
                    StartPrice = entryPx,
                    LookaheadEndTime = exitBar.StartTime,
                    EndPrice = exitPx,

                    ReturnPct = grossRet,
                    NetReturnPct = netRet,
                    DirAdjustedReturnPct = r.Direction == "BUY" ? grossRet : -grossRet,
                    DirAdjustedNetReturnPct = r.Direction == "BUY" ? netRet : -netRet,

                    MfePct = mfe,
                    MaePct = mae,
                    Outcome = outcome,
                    OutcomeDetail = outcomeDetail,
                    Notes = JsonSerializer.Serialize(new
                    {
                        sym,
                        tf,
                        r.Id,
                        r.Direction,
                        _p,
                        prices = new { entryPx, exitPx, targetPx, stopPx },
                        indices = new { entryIdx, lastIdx, horizonIdx = exitIdx },
                        ambiguousBars
                    })
                };

                evalBuffer.Add(eval);
            }

            if (evalBuffer.Count > 0)
            {
                // Upsert to be safe (index on RecoId+ConfigHash)
                var bc = new BulkConfig { SetOutputIdentity = false, OnConflictUpdateWhereSql = (tableAlias, entityAlias) => "1=1" };
                await dest.BulkInsertOrUpdateAsync(evalBuffer, bc);
                Console.WriteLine($"Scored {evalBuffer.Count} recos for {sym} {tf}.");
            }

            // ---- helpers ----
            static bool HasGap(List<Candle> c, int a, int b, TimeSpan step)
            {
                for (int i = a + 1; i <= b; i++)
                {
                    var dt = c[i].StartTime - c[i - 1].StartTime;
                    if (dt != step) return true;
                }
                return false;
            }

            static (decimal mfe, decimal mae) ComputeMfeMae(
                List<Candle> c, int a, int b, decimal startPx, string dir)
            {
                decimal denom = startPx == 0 ? 1 : startPx;
                decimal mfe = 0, mae = 0;

                if (dir == "BUY")
                {
                    decimal maxHigh = decimal.MinValue, minLow = decimal.MaxValue;
                    for (int i = a; i <= b; i++)
                    {
                        if (c[i].High > maxHigh) maxHigh = c[i].High;
                        if (c[i].Low < minLow) minLow = c[i].Low;
                    }
                    mfe = (maxHigh - startPx) / denom;
                    mae = (startPx - minLow) / denom;
                }
                else // SELL
                {
                    decimal maxHigh = decimal.MinValue, minLow = decimal.MaxValue;
                    for (int i = a; i <= b; i++)
                    {
                        if (c[i].High > maxHigh) maxHigh = c[i].High;
                        if (c[i].Low < minLow) minLow = c[i].Low;
                    }
                    mfe = (startPx - minLow) / denom;  // favorable down-move
                    mae = (maxHigh - startPx) / denom; // adverse up-move
                }

                if (mfe < 0) mfe = 0;
                if (mae < 0) mae = 0;
                return (mfe, mae);
            }

            static TimeSpan ParseTimeframe(string tf)
            {
                tf = tf.Trim().ToLowerInvariant();

                // simple aliases
                if (tf == "d" || tf == "1day" || tf == "day") return TimeSpan.FromDays(1);
                if (tf == "h" || tf == "1hour" || tf == "hour") return TimeSpan.FromHours(1);
                if (tf == "m" || tf == "1min" || tf == "min") return TimeSpan.FromMinutes(1);

                // split numeric part and suffix
                int i = 0;
                while (i < tf.Length && char.IsDigit(tf[i])) i++;
                if (i == 0) throw new ArgumentException($"Unsupported timeframe '{tf}'");

                if (!int.TryParse(tf[..i], out var n))
                    throw new ArgumentException($"Unsupported timeframe '{tf}'");

                var unit = tf[i..];

                return unit switch
                {
                    "m" or "min" or "mins" => TimeSpan.FromMinutes(n),
                    "h" or "hr" or "hrs" => TimeSpan.FromHours(n),
                    "d" or "day" or "days" => TimeSpan.FromDays(n),
                    "w" or "wk" or "wks" => TimeSpan.FromDays(7 * n),
                    "mo" or "mon" or "month" or "months"
                                                   => TimeSpan.FromDays(30 * n), // approximate month
                    "y" or "yr" or "yrs" or "year" or "years"
                                                   => TimeSpan.FromDays(365 * n), // approximate year
                    _ => throw new ArgumentException($"Unsupported timeframe '{tf}'")
                };
            }
        }

        private static string ComputeConfigHash(BacktestScoringParams p)
        {
            var s = $"{p.HorizonBars}|{p.EntryLagBars}|{p.EntryPrice}|{p.Exit}|{p.TargetPct}|{p.StopPct}|{p.FeeBpsPerSide}|{p.SlippageBpsPerSide}|{p.TieBreak}|{p.RequireContiguousData}";
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(s))).Substring(0, 32);
        }
    }
}

namespace BacktesterApp.Services
{
    public enum EntryPriceMode { Open, Close, Vwap60s }
    public enum ExitMode { HorizonClose, StopOrTargetElseHorizon }
    public enum TieBreakRule { StopFirst, TargetFirst, FavorProfit, MarkAmbiguous }

    public record BacktestScoringParams
    {
        public int HorizonBars { get; init; } = 20;
        public int EntryLagBars { get; init; } = 1;         // 1 => next bar entry (default)
        public EntryPriceMode EntryPrice { get; init; } = EntryPriceMode.Open;
        public ExitMode Exit { get; init; } = ExitMode.HorizonClose;

        // Optional stop/target (as fractions, e.g., 0.01m => 1%)
        public decimal? TargetPct { get; init; } = null;
        public decimal? StopPct { get; init; } = null;

        // Costs/slippage per side (bps). Round trip subtracts 2*(fee+slip)/10000.
        public decimal FeeBpsPerSide { get; init; } = 0;
        public decimal SlippageBpsPerSide { get; init; } = 0;

        // Edge handling
        public TieBreakRule TieBreak { get; init; } = TieBreakRule.TargetFirst;
        public bool RequireContiguousData { get; init; } = true;

        // Intake control
        public TimeSpan MinAge { get; init; } = TimeSpan.Zero;
        public int CandlePadBarsBefore { get; init; } = 2;  // pre-window safety
        public int CandlePadBarsAfter { get; init; } = 2;  // post-window safety
    }
}
