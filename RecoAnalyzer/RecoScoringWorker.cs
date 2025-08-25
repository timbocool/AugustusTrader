// RecoAnalyzer/RecoScoringWorker.cs
using Microsoft.EntityFrameworkCore;

namespace RecoAnalyzer
{
    public class RecoScoringWorker
    {
        private readonly AnalyzerDbContext _db;
        private readonly int _horizonBars;
        private readonly TimeSpan _minAge;

        public RecoScoringWorker(AnalyzerDbContext db, int horizonBars = 1, TimeSpan? minAge = null)
        {
            _db = db;
            _horizonBars = horizonBars;                  // 1 = “next bar”
            _minAge = minAge ?? TimeSpan.FromMinutes(15);
        }

        public void ScorePending(int batchSize = 2000)
        {
            // Select candidates older than cutoff that are not yet evaluated for this horizon
            var cutoff = DateTime.UtcNow.AddMinutes(-15);

            var candidates = _db.RecoLog
                .Where(r => r.Timestamp <= cutoff)
                .Where(r => !_db.RecoEvaluations
                    .Any(e => e.RecoId == r.Id && e.HorizonBars == _horizonBars))
                .OrderBy(r => r.Symbol)
                .ThenBy(r => r.Timeframe)
                .ThenBy(r => r.Timestamp)
                .Take(batchSize)              // e.g., 1000 if you want “TOP (1000)”
                .AsNoTracking()
                .ToList();


            if (candidates.Count == 0)
            {
                Console.WriteLine("No pending recos to score.");
                return;
            }

            Console.WriteLine($"Scoring {candidates.Count} recos (horizon={_horizonBars} bars, minAge={_minAge}).");

            // Work per (Symbol,Timeframe) to minimize queries
            foreach (var grp in candidates.GroupBy(r => new { r.Symbol, r.Timeframe }))
            {
                var sym = grp.Key.Symbol;
                var tf = grp.Key.Timeframe;

                var minTs = grp.Min(r => r.Timestamp);
                var maxTs = grp.Max(r => r.Timestamp);

                // Need candles strictly AFTER reco TS up to en+H bars
                var candles = _db.Candles
                    .Where(c => c.Symbol == sym && c.Timeframe == tf &&
                                c.StartTime > minTs.AddMinutes(-120) &&
                                c.StartTime <= maxTs.AddDays(7))   // generous pad
                    .OrderBy(c => c.StartTime)
                    .AsNoTracking()
                    .ToList();

                if (candles.Count == 0) continue;

                foreach (var r in grp)
                {
                    // Entry: first candle with StartTime > reco.Timestamp
                    int entryIdx = candles.FindIndex(c => c.StartTime > r.Timestamp);
                    if (entryIdx < 0) continue;

                    int exitIdx = entryIdx + _horizonBars;  // next bar for horizon=1
                    if (exitIdx >= candles.Count) continue; // not enough future bars yet

                    var entryBar = candles[entryIdx];
                    var exitBar = candles[exitIdx];

                    var entry = entryBar.Open;
                    var exit = exitBar.Close;

                    // Segment within horizon to compute MFE/MAE
                    var seg = candles.GetRange(entryIdx, _horizonBars);

                    var up = seg.Max(b => b.High) - entry;
                    var dn = entry - seg.Min(b => b.Low);
                    var denom = entry == 0 ? 1 : entry;
                    var mfe = up / denom;   // favorable
                    var mae = dn / denom;   // adverse

                    var ret = (exit - entry) / denom;
                    bool hit = r.Direction == "BUY" ? ret > 0 : ret < 0;

                    var eval = new RecoEvaluations
                    {
                        RecoId = r.Id,
                        Symbol = r.Symbol,
                        Timeframe = r.Timeframe,
                        EvaluatedAt = DateTime.UtcNow,
                        HorizonBars = _horizonBars,
                        EntryBarStartTime = entryBar.StartTime,
                        EntryPrice = entry,
                        ExitBarStartTime = exitBar.StartTime,
                        ExitPrice = exit,
                        Direction = r.Direction,
                        Confidence = r.Confidence,
                        ReturnPct = ret,
                        Hit = hit,
                        MFEPct = mfe,
                        MAEPct = mae,
                        Notes = "auto"
                    };

                    _db.RecoEvaluations.Add(eval);
                }

                _db.SaveChanges();
            }

            Console.WriteLine("Scoring complete.");
        }
    }
}
