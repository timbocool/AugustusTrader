// RecoAnalyzer/AnalyzerDbContext.cs
using Microsoft.EntityFrameworkCore;

namespace RecoAnalyzer
{
    public class AnalyzerDbContext : DbContext
    {
        public AnalyzerDbContext(DbContextOptions<AnalyzerDbContext> options) : base(options) { }

        public DbSet<RecoLog> RecoLog => Set<RecoLog>();
        public DbSet<Candle> Candles => Set<Candle>();
        public DbSet<RecoEvaluations> RecoEvaluations => Set<RecoEvaluations>();
        public DbSet<RecoAccuracySummary> RecoAccuracySummary { get; set; }
        //public DbSet<RecoAccuracySnapshot> RecoAccuracySnapshots { get; set; }
        public DbSet<RecoAccuracyHistory> RecoAccuracyHistory { get; set; }
        protected override void OnModelCreating(ModelBuilder b)
        {
            b.Entity<RecoLog>().ToTable("RecoLog", "dbo").HasKey(x => x.Id);
            b.Entity<Candle>().ToTable("Candles", "dbo").HasKey(x => x.Id);
            b.Entity<RecoEvaluations>().ToTable("RecoEvaluations", "dbo").HasKey(x => x.Id);

            b.Entity<RecoLog>().HasIndex(x => new { x.Symbol, x.Timeframe, x.Timestamp });
            b.Entity<Candle>().HasIndex(x => new { x.Symbol, x.Timeframe, x.StartTime });
            b.Entity<RecoEvaluations>().HasIndex(x => new { x.Symbol, x.Timeframe, x.HorizonBars });
            b.Entity<RecoEvaluations>().HasIndex(x => new { x.RecoId, x.HorizonBars }).IsUnique();
        }
    }

    public class RecoLog
    {
        public int Id { get; set; }
        public string Symbol { get; set; } = "";
        public string Timeframe { get; set; } = "";     // must match Candles.Timeframe values (e.g., "1m","5m","1h")
        public DateTime Timestamp { get; set; }         // bar close time
        public string Detector { get; set; } = "";
        public string Direction { get; set; } = "";     // "BUY" | "SELL"
        public decimal Confidence { get; set; }
        public decimal Price { get; set; }
        public string? Context { get; set; }
    }

    public class Candle
    {
        public long Id { get; set; }
        public string Symbol { get; set; } = "";
        public string Timeframe { get; set; } = ""; // e.g. "1m","5m","1h","1d"
        public DateTime StartTime { get; set; }
        public decimal Open { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
        public decimal Close { get; set; }
        public decimal? Volume { get; set; }
        public decimal? Vwap60s { get; set; }
    }

    public class RecoEvaluations
    {
        public long Id { get; set; }
        public int RecoId { get; set; }

        public string? Symbol { get; set; }
        public string? Timeframe { get; set; }
        public DateTime EvaluatedAt { get; set; }   // always known
        public int HorizonBars { get; set; }        // always known

        public DateTime? EntryBarStartTime { get; set; }
        public decimal? EntryPrice { get; set; }
        public DateTime? ExitBarStartTime { get; set; }
        public decimal? ExitPrice { get; set; }

        public string? Direction { get; set; }
        public decimal? Confidence { get; set; }
        public decimal? ReturnPct { get; set; }
        public bool? Hit { get; set; }

        public decimal? MFEPct { get; set; }
        public decimal? MAEPct { get; set; }

        public string? Notes { get; set; }
    }

    public class RecoAccuracySnapshot
    {
        public long Id { get; set; }
        public DateTime CapturedAtUtc { get; set; }
        public string Strategy { get; set; } = "";
        public string Timeframe { get; set; } = "";
        public int? Total { get; set; }
        public int? Hits { get; set; }
        public double? Accuracy { get; set; }
        public double? AvgConfidence { get; set; }
    }

    public class RecoAccuracySummary
    {
        public long Id { get; set; }
        public string Strategy { get; set; } = "";
        public string Timeframe { get; set; } = "";
        public int? Total { get; set; }
        public int? Hits { get; set; }
        public double? Accuracy { get; set; }
        public double? AvgConfidence { get; set; }
        public DateTime LastUpdatedUtc { get; set; }
    }

    public class RecoAccuracyHistory
    {
        public long Id { get; set; }
        public string Strategy { get; set; } = "";
        public string Timeframe { get; set; } = "";
        public int? Total { get; set; }
        public int? Hits { get; set; }
        public double? Accuracy { get; set; }
        public double? AvgConfidence { get; set; }
        public DateTime SnapshotUtc { get; set; }
    }



}
