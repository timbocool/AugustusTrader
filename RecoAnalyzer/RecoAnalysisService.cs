using Microsoft.EntityFrameworkCore;

namespace RecoAnalyzer
{
    public class RecoAnalysisService
    {
        private readonly AnalyzerDbContext _db;

        public RecoAnalysisService(AnalyzerDbContext db)
        {
            _db = db;
        }

        public void RunAnalysis()
        {
            // Pull last 1000 records
            var recos = _db.RecoLog
                .OrderByDescending(r => r.Timestamp)
                .Take(1000)
                .ToList();

            Console.WriteLine($"Loaded {recos.Count} reco logs.");

            // Example 1: Count BUY vs SELL
            var buyCount = recos.Count(r => r.Direction == "BUY");
            var sellCount = recos.Count(r => r.Direction == "SELL");
            Console.WriteLine($"BUY: {buyCount}, SELL: {sellCount}");

            // Example 2: Average confidence
            var avgConf = recos.Average(r => (double)r.Confidence);
            Console.WriteLine($"Avg confidence: {avgConf:F2}");

            // Example 3: Group by Detector
            var grouped = recos
                .GroupBy(r => r.Detector)
                .Select(g => new { Detector = g.Key, Count = g.Count(), AvgConf = g.Average(r => r.Confidence) })
                .OrderByDescending(g => g.Count);

            Console.WriteLine("Top detectors:");
            foreach (var g in grouped)
            {
                Console.WriteLine($"{g.Detector,-25} {g.Count,5} recos avg conf={g.AvgConf:F2}");
            }

            // Example 4: Symbol trend
            var bySymbol = recos.GroupBy(r => r.Symbol);
            foreach (var symGroup in bySymbol)
            {
                var buys = symGroup.Count(r => r.Direction == "BUY");
                var sells = symGroup.Count(r => r.Direction == "SELL");
                Console.WriteLine($"{symGroup.Key}: BUY {buys}, SELL {sells}");
            }
        }
    }
}
