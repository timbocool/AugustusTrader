using System.ComponentModel.DataAnnotations;

public class SymbolTradeLimit
{
    [Key]
    public int SymbolTradeLimitId { get; set; }
    public string? Symbol { get; set; }
    public int PriceScale { get; set; }
    public int QuantityScale { get; set; }
    public int AmountScale { get; set; }
    public string? MinQuantity { get; set; }
    public string? MinAmount { get; set; }
    public string? MaxQuantity { get; set; }
    public string? MaxAmount { get; set; }
    public string? HighestBid { get; set; }
    public string? LowestAsk { get; set; }
}
