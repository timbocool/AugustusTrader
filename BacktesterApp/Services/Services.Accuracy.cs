using EFCore.BulkExtensions;
using Microsoft.EntityFrameworkCore;
using Shared;
using Shared.Models;

namespace BacktesterApp.Services
{
    public class StrategyAccuracyUpdater
    {
        private readonly DbContextOptions<SharedDbContext> _options;
        public StrategyAccuracyUpdater(DbContextOptions<SharedDbContext> options)
        {
            _options = options;
        }

        public async Task UpdateSingleQueryAsync(string? configHash = null)
        {
            await using var db = new SharedDbContext(_options);
            var now = DateTime.UtcNow;

            // Default: most recent run
            if (string.IsNullOrEmpty(configHash))
            {
                configHash = await db.RecoEvaluation
                    .OrderByDescending(e => e.EvaluatedAt)
                    .Select(e => e.ConfigHash)
                    .FirstOrDefaultAsync();
            }

            var grouped = await db.RecoLog
                .Join(db.RecoEvaluation,
                      r => r.Id,
                      e => e.RecoId,
                      (r, e) => new { r.Detector, r.Timeframe, r.Confidence, e.DirAdjustedNetReturnPct, e.Outcome, e.ConfigHash })
                .Where(x => x.ConfigHash == configHash)
                .GroupBy(x => new { x.Detector, x.Timeframe })
                .ToListAsync();

            var summaries = grouped.Select(g =>
            {
                var returns = g.Select(x => (double)(x.DirAdjustedNetReturnPct ?? 0m)).ToList();
                int total = returns.Count;
                int wins = returns.Count(r => r > 0);
                int losses = returns.Count(r => r < 0);

                double avg = returns.Average();
                double median = returns.OrderBy(r => r).Skip(total / 2).First();

                double avgWin = wins > 0 ? returns.Where(r => r > 0).Average() : 0;
                double avgLoss = losses > 0 ? returns.Where(r => r < 0).Average() : 0;

                double winRate = total > 0 ? (double)wins / total : 0;
                double lossRate = total > 0 ? (double)losses / total : 0;

                double expectancy = winRate * avgWin - lossRate * Math.Abs(avgLoss);
                double payoffRatio = avgLoss != 0 ? avgWin / Math.Abs(avgLoss) : 0;
                double winLossRatio = losses > 0 ? (double)wins / losses : wins;

                return new RecoStrategyAccuracy
                {
                    Strategy = g.Key.Detector,
                    Timeframe = g.Key.Timeframe,
                    ConfigHash = configHash!,
                    Total = total,
                    Hits = wins,
                    Accuracy = winRate,
                    AvgConfidence = g.Average(x => (double)x.Confidence),
                    AvgNetReturnPct = avg,
                    MedianNetReturnPct = median,
                    Expectancy = expectancy,
                    PayoffRatio = payoffRatio,
                    WinLossRatio = winLossRatio,
                    LastUpdatedUtc = now
                };
            }).ToList();

            if (summaries.Count == 0)
            {
                Console.WriteLine("No strategy accuracy data to update.");
                return;
            }

            await db.BulkInsertOrUpdateAsync(summaries, new BulkConfig
            {
                UpdateByProperties = new List<string>
        {
            nameof(RecoStrategyAccuracy.Strategy),
            nameof(RecoStrategyAccuracy.Timeframe),
            nameof(RecoStrategyAccuracy.ConfigHash)
        }
            });

            Console.WriteLine($"[SingleQuery] Updated strategy accuracy for {summaries.Count} strategy/timeframes (ConfigHash={configHash}).");
        }
    }
}
