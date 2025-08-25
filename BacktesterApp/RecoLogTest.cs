using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BacktesterStandalone.Models
{
    // --- Reco logs (TEST) ---
    [Table("RecoLogTest", Schema = "dbo")]
    public class RecoLogTest
    {
        public long Id { get; set; }
        [MaxLength(50)] public string Symbol { get; set; } = "";
        [MaxLength(20)] public string Timeframe { get; set; } = "";
        public DateTime Timestamp { get; set; }  // when reco fired (bar StartTime). Next bar is entry.
        [MaxLength(100)] public string Detector { get; set; } = "";
        [MaxLength(10)] public string Direction { get; set; } = ""; // BUY/SELL
        public decimal Confidence { get; set; }
        public decimal Price { get; set; }
        public string? Context { get; set; }
    }

}