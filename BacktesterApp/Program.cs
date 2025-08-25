using Microsoft.EntityFrameworkCore;
using Shared;
using BacktesterApp.Services;

var conn = "Server=localhost;Database=Tradexus;Trusted_Connection=True;Encrypt=False;";

// context options
var analyzerOptions = new DbContextOptionsBuilder<SharedDbContext>()
    .UseSqlServer(conn).Options;
var backtestOptions = new DbContextOptionsBuilder<SharedDbContext>()
    .UseSqlServer(conn).Options;

// --- STEP 1: discover timeframes ---
// config
string symbol = "XRP_USDT";
DateTime start = new(2025, 8, 1);
DateTime end = new(2025, 8, 25);
int horizonBars = 1;
TimeSpan minAge = TimeSpan.Zero;
int maxParallel = 2; // tune based on hardware

//---STEP 1: discover timeframes ---
using (var srcProbe = new SharedDbContext(analyzerOptions))
{
    var timeframes = srcProbe.Candles
        .Where(c => c.Symbol == symbol && c.StartTime >= start && c.StartTime <= end)
        .Select(c => c.Timeframe)
        .Distinct()
        .ToList();

    Console.WriteLine($"Backtesting {symbol} across {timeframes.Count} TFs from {start:yyyy-MM-dd} to {end:yyyy-MM-dd}...");

    foreach (var tf in timeframes)
    {
        Console.WriteLine($"→ Running detectors for TF={tf} in weekly slices...");

        // --- figure out resumeStart by looking only at BacktestDbContext ---
        await using (var destProbe = new SharedDbContext(backtestOptions))
        {
            var lastTimestamp = await destProbe.RecoLog
                .Where(r => r.Symbol == symbol && r.Timeframe == tf)
                .MaxAsync(r => (DateTime?)r.Timestamp);

            var effectiveStart = lastTimestamp.HasValue
                ? lastTimestamp.Value.AddSeconds(1)
                : start;

            // --- build weekly slices (7 days per slice) ---
            var slices = BuildWeeklySlices(effectiveStart, end, 7);

            // --- run slices in parallel (only backtest context is partitioned) ---
            using var semaphore = new SemaphoreSlim(maxParallel);
            var tasks = slices.Select(async slice =>
            {
                await semaphore.WaitAsync();
                try
                {
                    await using var src = new SharedDbContext(analyzerOptions); // reader
                    await using var dest = new SharedDbContext(backtestOptions);     // writer
                    var runner = new BacktestRunner(src, dest);

                    Console.WriteLine($"   Slice {slice.start:yyyy-MM-dd} → {slice.end:yyyy-MM-dd}");

                    await runner.RunAsync(symbol, tf, slice.start, slice.end);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);
        }
    }
}
// --- STEP 2: scoring (also operates only on BacktestDbContext slices/batches) ---
using (var src = new SharedDbContext(analyzerOptions))
// --- STEP 2: scoring (also operates only on BacktestDbContext slices/batches) ---
{
    // --- STEP 2: scoring (operates on BacktestDbContext) ---
    // Use the parameterized worker for correctness + run isolation
    //var scoringParams = new BacktestScoringParams
    //{
    //    HorizonBars = horizonBars,
    //    EntryLagBars = 1, // enter next bar to avoid lookahead
    //    EntryPrice = EntryPriceMode.Open,
    //    Exit = ExitMode.HorizonClose, // same behavior as your original run
    //    // If you want stop/target pathing, set Exit = StopOrTargetElseHorizon and fill TargetPct/StopPct
    //    //TargetPct = 0.01m,   // 1% profit target
    //    //StopPct = 0.005m,  // 0.5% stop loss
    //    TargetPct = 0.05m,   // 5% profit target
    //    StopPct = 0.005m,  // 0.5% stop loss
    //    FeeBpsPerSide = 0.5m, // adjust to venue
    //    SlippageBpsPerSide = 1.5m, // adjust to venue
    //    RequireContiguousData = true,
    //    MinAge = minAge,
    //    CandlePadBarsBefore = 2,
    //    CandlePadBarsAfter = 2
    //};

    var intradayScoring = new BacktestScoringParams
    {
        HorizonBars = 20,                   // hold up to 20 bars (e.g. 20 minutes on 1m TF)
        EntryLagBars = 1,
        EntryPrice = EntryPriceMode.Open,
        Exit = ExitMode.StopOrTargetElseHorizon,

        TargetPct = 0.003m,                 // 0.3% take-profit
        StopPct = 0.002m,                 // 0.2% stop-loss

        FeeBpsPerSide = 0.5m,
        SlippageBpsPerSide = 1.0m,
        RequireContiguousData = true
    };


    var scorer = new BacktestScoringWorker(analyzerOptions, backtestOptions, intradayScoring);
    Console.WriteLine($"Scoring pending recos with config {scorer.ConfigHash}...");
    await scorer.ScoreAllPendingAsync(batchSize: 10000, maxParallel: maxParallel);

    // --- STEP 3: accuracy ---
    var accuracy = new StrategyAccuracyUpdater(backtestOptions);
    await accuracy.UpdateSingleQueryAsync(null);
}

Console.WriteLine("Done.");

// --- helper to split a date range into weekly slices ---
static List<(DateTime start, DateTime end)> BuildWeeklySlices(DateTime start, DateTime end, int daysPerSlice = 7)
{
    var slices = new List<(DateTime start, DateTime end)>();
    var current = start;

    while (current <= end)
    {
        var sliceStart = current;
        var sliceEnd = current.AddDays(daysPerSlice).AddSeconds(-1);

        if (sliceEnd > end) sliceEnd = end;

        slices.Add((sliceStart, sliceEnd));
        current = current.AddDays(daysPerSlice);
    }

    return slices;
}