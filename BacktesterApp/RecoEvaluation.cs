using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BacktesterStandalone.Models
{
    // --- Reco evaluations (TEST) ---
    [Table("RecoEvaluation", Schema = "dbo")]
    [Index(nameof(RecoId), nameof(ConfigHash), IsUnique = true)]
    public class RecoEvaluation
    {
        public long Id { get; set; }
        public long RecoId { get; set; }

        // Identify a run (so you can store multiple evaluations per reco)
        [MaxLength(64)] public string ConfigHash { get; set; } = "";

        public int HorizonBars { get; set; }
        public DateTime EvaluatedAt { get; set; }

        public DateTime? EntryBarStartTime { get; set; }
        public decimal? StartPrice { get; set; }
        public DateTime? LookaheadEndTime { get; set; }
        public decimal? EndPrice { get; set; }

        public decimal? ReturnPct { get; set; }             // gross
        public decimal? NetReturnPct { get; set; }          // after fees/slippage (round-trip)
        public decimal? DirAdjustedReturnPct { get; set; }  // BUY=ret, SELL=-ret (gross)
        public decimal? DirAdjustedNetReturnPct { get; set; } // BUY=net, SELL=-net
        public decimal? MfePct { get; set; }
        public decimal? MaePct { get; set; }

        [MaxLength(30)] public string? Outcome { get; set; }      // TARGET/STOP/HORIZON/GAP/AMBIG/UNKNOWN
        [MaxLength(100)] public string? OutcomeDetail { get; set; } // e.g. "target-first", "horizon-close"
        public string? Notes { get; set; } // JSON run metadata
    }

}