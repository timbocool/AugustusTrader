using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Shared.Models
{


    [Table("RecoStrategyAccuracy", Schema = "dbo")]
    [Index(nameof(Strategy), nameof(Timeframe), nameof(ConfigHash), IsUnique = true)]
    public class RecoStrategyAccuracy
    {
        public long Id { get; set; }

        [MaxLength(100)] public string Strategy { get; set; } = "";
        [MaxLength(20)] public string Timeframe { get; set; } = "";

        // Link to scoring run
        [MaxLength(64)] public string ConfigHash { get; set; } = "";

        public int Total { get; set; }
        public int Hits { get; set; }
        public double Accuracy { get; set; }            // Hits / Total
        public double AvgConfidence { get; set; }

        // Profitability stats
        public double AvgNetReturnPct { get; set; }     // mean of DirAdjustedNetReturnPct
        public double MedianNetReturnPct { get; set; }  // median
        public double Expectancy { get; set; }          // (Win% * AvgWin) - (Loss% * AvgLoss)
        public double PayoffRatio { get; set; }         // AvgWin / |AvgLoss|

        public double WinLossRatio { get; set; }        // #Wins / #Losses

        public DateTime LastUpdatedUtc { get; set; }
    }

}