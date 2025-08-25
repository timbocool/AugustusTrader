namespace RecoPaperTrader;

public static class TimeframeUtils
{
    public static TimeSpan Parse(string tf)
    {
        if (string.IsNullOrWhiteSpace(tf)) throw new ArgumentException("Timeframe missing");
        tf = tf.Trim().ToLowerInvariant();

        if (tf.EndsWith("m") && int.TryParse(tf[..^1], out var m) && m > 0) return TimeSpan.FromMinutes(m);
        if (tf.EndsWith("h") && int.TryParse(tf[..^1], out var h) && h > 0) return TimeSpan.FromHours(h);
        if (tf.EndsWith("d") && int.TryParse(tf[..^1], out var d) && d > 0) return TimeSpan.FromDays(d);
        if (tf.EndsWith("w") && int.TryParse(tf[..^1], out var w) && w > 0) return TimeSpan.FromDays(7 * w);
        if (tf.EndsWith("mo") && int.TryParse(tf[..^2], out var mo) && mo > 0) return TimeSpan.FromDays(30 * mo); // approx
        if (tf.EndsWith("y") && int.TryParse(tf[..^1], out var y) && y > 0) return TimeSpan.FromDays(365 * y);    // approx

        if (tf == "1" || tf == "1d" || tf == "d") return TimeSpan.FromDays(1);
        throw new ArgumentException($"Unsupported timeframe '{tf}'");
    }
}
