using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RecoPaperTrader;

[Table("PaperPosition", Schema = "dbo")]
public class PaperPosition
{
    public long Id { get; set; }
    [MaxLength(100)] public string AccountName { get; set; } = "Paper-Default";
    [MaxLength(50)] public string Symbol { get; set; } = "";
    [MaxLength(20)] public string Timeframe { get; set; } = "";
    [MaxLength(100)] public string Strategy { get; set; } = "";
    [MaxLength(10)] public string Direction { get; set; } = ""; // BUY/SELL
    public decimal Quantity { get; set; }
    public decimal EntryPrice { get; set; }
    public DateTime EntryTsUtc { get; set; }
    public DateTime EntryBarStartTime { get; set; }
    public int HorizonBars { get; set; }
    public DateTime ExitDueBarStartTime { get; set; }
    [MaxLength(64)] public string ConfigHash { get; set; } = "";
    [MaxLength(20)] public string Status { get; set; } = "OPEN";
}
