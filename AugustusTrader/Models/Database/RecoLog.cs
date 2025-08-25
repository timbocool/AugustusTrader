using System.ComponentModel.DataAnnotations;

public class RecoLog
{
    public int Id { get; set; } // PK
    public string Symbol { get; set; } = string.Empty;
    public string Timeframe { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string Detector { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public decimal Confidence { get; set; }
    public decimal Price { get; set; }
    public string? Context { get; set; }
}
