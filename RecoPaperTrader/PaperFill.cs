using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RecoPaperTrader;

[Table("PaperFill", Schema = "dbo")]
public class PaperFill
{
    public long Id { get; set; }
    [MaxLength(100)] public string AccountName { get; set; } = "Paper-Default";
    public long? PositionId { get; set; }
    [MaxLength(4)] public string Side { get; set; } = ""; // BUY/SELL
    [MaxLength(50)] public string Symbol { get; set; } = "";
    [MaxLength(20)] public string Timeframe { get; set; } = "";
    [MaxLength(100)] public string Strategy { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal Price { get; set; }
    public decimal Fee { get; set; }
    public decimal Slippage { get; set; }
    public DateTime FilledUtc { get; set; }
    public long? SourceRecoId { get; set; }
    public string? Notes { get; set; }
}
