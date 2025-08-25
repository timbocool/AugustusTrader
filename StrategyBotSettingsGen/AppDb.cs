using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StrategyBotSettingsGen;

public class BacktestDbContext : DbContext
{
    public BacktestDbContext(DbContextOptions<BacktestDbContext> options) : base(options) {}

    public DbSet<RecoStrategyAccuracy> RecoStrategyAccuracy => Set<RecoStrategyAccuracy>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {

    }
}

[Table("RecoStrategyAccuracy", Schema = "dbo")]
public class RecoStrategyAccuracy
{
    public long Id { get; set; }

    [MaxLength(100)] public string Strategy { get; set; } = "";
    [MaxLength(20)]  public string Timeframe { get; set; } = "";

    [MaxLength(64)]  public string ConfigHash { get; set; } = "";

    public int Total { get; set; }
    public int Hits  { get; set; }
    public decimal Accuracy { get; set; }
    public decimal? AvgConfidence { get; set; }

    // Profitability stats
    public double AvgNetReturnPct { get; set; }
    public double MedianNetReturnPct { get; set; }
    public double Expectancy { get; set; }
    public double PayoffRatio { get; set; }
    public double WinLossRatio { get; set; }

    public DateTime LastUpdatedUtc { get; set; }
}
