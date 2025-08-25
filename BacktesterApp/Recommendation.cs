namespace BacktesterStandalone.Engine
{
    public record Recommendation(
        string Symbol,
        System.TimeSpan Timeframe,
        string Pattern,
        string Direction,            // "BUY" or "SELL"
        decimal Confidence,
        decimal RefPrice,
        string? Note
    );
}