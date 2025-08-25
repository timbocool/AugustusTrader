using System;

namespace Shared.Models
{
    public sealed record Recommendation(
        string Symbol,
        TimeSpan Timeframe,
        string Pattern,
        string Direction,
        decimal Confidence,
        decimal RefPrice,
        string? Note = null
    );
}
