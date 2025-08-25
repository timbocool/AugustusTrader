using System;
using System.Linq;

namespace RecoAnalyzer
{
    public class RecoAccuracyService
    {
        private readonly AnalyzerDbContext _db;

        public RecoAccuracyService(AnalyzerDbContext db)
        {
            _db = db;
        }

        public void RunAnalysis()
        {
            try
            {
                var grouped = _db.RecoLog
               .Join(
                   _db.RecoEvaluations,
                   r => r.Id,
                   e => e.RecoId,
                   (r, e) => new { r.Detector, r.Timeframe, e.Hit, r.Confidence }
               )
               .Where(x => x.Hit != null)
               .GroupBy(x => new { x.Detector, x.Timeframe })
               .Select(g => new
               {
                   Strategy = g.Key.Detector,
                   Timeframe = g.Key.Timeframe,
                   Total = g.Count(),
                   Hits = g.Count(x => x.Hit == true),
                   Accuracy = (double)g.Count(x => x.Hit == true) / g.Count(),
                   AvgConfidence = g.Average(x => (double)x.Confidence)
               })
               .ToList();

                var now = DateTime.UtcNow;

                foreach (var g in grouped)
                {
                    // --- Update Summary (unique per strategy/timeframe) ---
                    var existing = _db.RecoAccuracySummary
                        .FirstOrDefault(s => s.Strategy == g.Strategy && s.Timeframe == g.Timeframe);

                    if (existing == null)
                    {
                        _db.RecoAccuracySummary.Add(new RecoAccuracySummary
                        {
                            Strategy = g.Strategy,
                            Timeframe = g.Timeframe,
                            Total = g.Total,
                            Hits = g.Hits,
                            Accuracy = g.Accuracy,
                            AvgConfidence = g.AvgConfidence,
                            LastUpdatedUtc = now
                        });
                    }
                    else
                    {
                        existing.Total = g.Total;
                        existing.Hits = g.Hits;
                        existing.Accuracy = g.Accuracy;
                        existing.AvgConfidence = g.AvgConfidence;
                        existing.LastUpdatedUtc = now;
                    }

                    // --- Always insert a History snapshot ---
                    _db.RecoAccuracyHistory.Add(new RecoAccuracyHistory
                    {
                        Strategy = g.Strategy,
                        Timeframe = g.Timeframe,
                        Total = g.Total,
                        Hits = g.Hits,
                        Accuracy = g.Accuracy,
                        AvgConfidence = g.AvgConfidence,
                        SnapshotUtc = now
                    });
                }

                _db.SaveChanges();

                Console.WriteLine($"Updated {grouped.Count} summaries and appended history at {now:u}");
            }
            catch (Exception ex)
            {

                throw;
            }
           
        }

    }
}
