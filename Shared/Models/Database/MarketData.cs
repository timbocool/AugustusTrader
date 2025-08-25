using System.ComponentModel.DataAnnotations;

public class MarketData
{
    [Key]
    public int RecordId { get; set; }
    public string Symbol { get; set; }
    public string BaseCurrencyName { get; set; }
    public string QuoteCurrencyName { get; set; }
    public string DisplayName { get; set; }
    public string State { get; set; }
    public long VisibleStartTime { get; set; }
    public long TradableStartTime { get; set; }

    public SymbolTradeLimit SymbolTradeLimit { get; set; }
    public CrossMargin CrossMargin { get; set; }

    public bool ShowAdditionalInfo { get; set; }
}
