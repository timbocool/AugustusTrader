using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace StrategyBotSettingsGen;

public static class BotSettingsBuilder
{
    public record Thresholds(
        decimal MinExpectancy5m15m,
        decimal MinExpectancy30m1h,
        decimal MinExpectancy2h6h,
        double MinAccuracy,
        double MinConfidence,
        int MinTotal5m,
        int MinTotal15m,
        int MinTotal30m1h,
        int MinTotal2h6h
    );

    public record Risk(
        decimal RiskPctPerTrade,
        decimal MaxDailyLossPct,
        int MaxOpenPositionsPerSymbol,
        int CooldownMinutesPerSymbol
    );

    public record StrategySetting(
        string Strategy,
        string Timeframe,
        double Accuracy,
        double AvgConfidence,
        double AvgNetReturnPct,
        double MedianNetReturnPct,
        double Expectancy,
        double PayoffRatio,
        double WinLossRatio
    );

    public record BotSettings(
        string ConfigHash,
        List<StrategySetting> Strategies,
        Risk Risk
    );

    public static async Task<string> BuildAsync(
        DbContextOptions<BacktestDbContext> options,
        string configHash,
        Thresholds t,
        Risk r,
        string outputPath
    )
    {
        await using var db = new BacktestDbContext(options);

        // Query RecoStrategyAccuracy filtered by config hash
        var rows = await db.RecoStrategyAccuracy
            .Where(x => x.ConfigHash == configHash)
            .ToListAsync();

        if (rows.Count == 0)
        {
            Console.Error.WriteLine($"No strategy accuracy rows found for ConfigHash={configHash}.");
            return "";
        }

        // Apply thresholds
        var selected = new List<StrategySetting>();
        foreach (var row in rows)
        {
            if ((double)row.Accuracy < t.MinAccuracy) continue;
            if ((double)(row.AvgConfidence ?? 0m) < t.MinConfidence) continue;

            int total = row.Total;
            decimal minExp = row.Timeframe switch
            {
                "5m" or "15m" => t.MinExpectancy5m15m,
                "30m" or "1h" => t.MinExpectancy30m1h,
                "2h" or "6h" => t.MinExpectancy2h6h,
                _ => 0m
            };

            int minTotal = row.Timeframe switch
            {
                "5m" => t.MinTotal5m,
                "15m" => t.MinTotal15m,
                "30m" or "1h" => t.MinTotal30m1h,
                "2h" or "6h" => t.MinTotal2h6h,
                _ => 0
            };

            if (total < minTotal) continue;
            if ((decimal)row.Expectancy < minExp) continue;

            selected.Add(new StrategySetting(
                Strategy: row.Strategy,
                Timeframe: row.Timeframe,
                Accuracy: (double)row.Accuracy,
                AvgConfidence: (double)(row.AvgConfidence ?? 0m),
                AvgNetReturnPct: row.AvgNetReturnPct,
                MedianNetReturnPct: row.MedianNetReturnPct,
                Expectancy: row.Expectancy,
                PayoffRatio: row.PayoffRatio,
                WinLossRatio: row.WinLossRatio
            ));
        }

        var settings = new BotSettings(
            ConfigHash: configHash,
            Strategies: selected.OrderByDescending(x => x.Expectancy).ToList(),
            Risk: r
        );

        // Write JSON
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(outputPath, json);

        return outputPath;
    }
}
