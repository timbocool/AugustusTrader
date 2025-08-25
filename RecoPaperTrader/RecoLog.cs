using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RecoPaperTrader;

[Table("RecoLog", Schema = "dbo")]
public class RecoLog
{
    public long Id { get; set; }
    [MaxLength(50)] public string Symbol { get; set; } = "";
    [MaxLength(20)] public string Timeframe { get; set; } = "";
    public DateTime Timestamp { get; set; }
    [MaxLength(100)] public string Detector { get; set; } = "";
    [MaxLength(10)] public string Direction { get; set; } = "";
    public decimal Confidence { get; set; }
    public decimal Price { get; set; }
    public string? Context { get; set; }
}
