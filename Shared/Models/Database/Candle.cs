using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

//public class Candles
//{
//    public long Id { get; set; }
//    public string Symbol { get; set; } = string.Empty;
//    public string Timeframe { get; set; } = string.Empty; // "1m", "5m", "1h", etc.
//    public DateTime StartTime { get; set; }
//    public decimal Open { get; set; }
//    public decimal High { get; set; }
//    public decimal Low { get; set; }
//    public decimal Close { get; set; }
//    public decimal? Volume { get; set; }
//    public decimal? Vwap60s { get; set; }
//}

[Table("Candles", Schema = "dbo")]
public class Candle
{
    public long Id { get; set; }
    [MaxLength(50)] public string Symbol { get; set; } = "";
    [MaxLength(20)] public string Timeframe { get; set; } = "";
    public DateTime StartTime { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public decimal? Volume { get; set; }
    public decimal? Vwap60s { get; set; }
}