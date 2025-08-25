using Microsoft.EntityFrameworkCore;
using RecoAnalyzer;

var options = new DbContextOptionsBuilder<AnalyzerDbContext>()
    .UseSqlServer("Server=localhost;Database=Tradexus;Trusted_Connection=True;Encrypt=False;")
    .Options;

using var db = new AnalyzerDbContext(options);

// Services
var scorer = new RecoScoringWorker(db, horizonBars: 1, minAge: TimeSpan.FromMinutes(15));
var accuracy = new RecoAccuracyService(db);

// Loop forever, run every minute
while (true)
{
    try
    {
        Console.WriteLine($"[{DateTime.UtcNow}] Running scoring + accuracy analysis...");

        scorer.ScorePending();
        accuracy.RunAnalysis();

        Console.WriteLine("Cycle complete, sleeping 60s...");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error: {ex.Message}");
    }

    Thread.Sleep(TimeSpan.FromMinutes(1));
}
