using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using StrategyBotSettingsGen;

var builder = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory) // needs FileExtensions package
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables();

var config = builder.Build();

// CLI parsing (very light)
string? GetArg(string key, string shortKey = "")
{
    for (int i = 0; i < args.Length; i++)
    {
        if (args[i] == key || (!string.IsNullOrWhiteSpace(shortKey) && args[i] == shortKey))
        {
            if (i + 1 < args.Length) return args[i + 1];
        }
        if (args[i].StartsWith(key + "=")) return args[i][(key + "=").Length..];
    }
    return null;
}

void PrintHelp()
{
    Console.WriteLine("""
Usage:
  dotnet run -- --config <ConfigHash> [--out bot.settings.json]
              [--min-accuracy 0.60] [--min-confidence 0.55]
              [--min-exp-5m15m 0.002] [--min-exp-30m1h 0.003] [--min-exp-2h6h 0.007]
              [--min-total-5m 5000] [--min-total-15m 500] [--min-total-30m1h 300] [--min-total-2h6h 100]
              [--risk-pct 0.0025] [--max-dd-daily 0.015] [--max-open 1] [--cooldown 60]

Connection string sources (in order):
  1) env TRX_CONN
  2) appsettings.json: ConnectionStrings:Tradexus

Examples:
  dotnet run -- --config 0EF7E17F34C6C168C63F5165F680A87D
  dotnet run -- --config 0EF7... --out settings.json --min-accuracy 0.62 --risk-pct 0.0015
""");
}

if (args.Contains("--help") || args.Contains("-h") || args.Contains("/?"))
{
    PrintHelp();
    return;
}

var configHash = GetArg("--config", "-c") ?? Environment.GetEnvironmentVariable("CONFIG_HASH");
if (string.IsNullOrWhiteSpace(configHash))
{
    Console.Write("Enter ConfigHash: ");
    configHash = Console.ReadLine();
}
if (string.IsNullOrWhiteSpace(configHash))
{
    Console.Error.WriteLine("ConfigHash is required. Use --config <hash> or set CONFIG_HASH env var.");
    return;
}

// where to save output
string fileName = GetArg("--out") ?? $"bot.settings.{configHash}.json";
string outputPath;

// if --out is absolute, use it directly, otherwise put it on Desktop
if (Path.IsPathRooted(fileName))
{
    outputPath = fileName;
}
else
{
    string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    outputPath = Path.Combine(desktop, fileName);
}

// thresholds (defaults)
double minAccuracy = double.TryParse(GetArg("--min-accuracy") ?? "", out var dAcc) ? dAcc : 0.60;
double minConfidence = double.TryParse(GetArg("--min-confidence") ?? "", out var dConf) ? dConf : 0.55;
decimal minExp5m15m = decimal.TryParse(GetArg("--min-exp-5m15m") ?? "", out var de1) ? de1 : 0.002m;
decimal minExp30m1h = decimal.TryParse(GetArg("--min-exp-30m1h") ?? "", out var de2) ? de2 : 0.003m;
decimal minExp2h6h = decimal.TryParse(GetArg("--min-exp-2h6h") ?? "", out var de3) ? de3 : 0.007m;
int minTotal5m = int.TryParse(GetArg("--min-total-5m") ?? "", out var it1) ? it1 : 5000;
int minTotal15m = int.TryParse(GetArg("--min-total-15m") ?? "", out var it2) ? it2 : 500;
int minTotal30m1h = int.TryParse(GetArg("--min-total-30m1h") ?? "", out var it3) ? it3 : 300;
int minTotal2h6h = int.TryParse(GetArg("--min-total-2h6h") ?? "", out var it4) ? it4 : 100;

// risk params (defaults)
decimal riskPct = decimal.TryParse(GetArg("--risk-pct") ?? "", out var rp) ? rp : 0.0025m;
decimal maxDailyLossPct = decimal.TryParse(GetArg("--max-dd-daily") ?? "", out var dd) ? dd : 0.015m;
int maxOpenPerSymbol = int.TryParse(GetArg("--max-open") ?? "", out var mo) ? mo : 1;
int cooldownMinutes = int.TryParse(GetArg("--cooldown") ?? "", out var cd) ? cd : 60;

// connection string
var conn = Environment.GetEnvironmentVariable("TRX_CONN")
        ?? config.GetConnectionString("Tradexus");

if (string.IsNullOrWhiteSpace(conn))
{
    Console.Error.WriteLine("Connection string missing. Set TRX_CONN env var or add appsettings.json ConnectionStrings:Tradexus.");
    return;
}

// EF options
var dbOptions = new DbContextOptionsBuilder<BacktestDbContext>()
    .UseSqlServer(conn)
    .Options;

// Build settings
var thresholds = new BotSettingsBuilder.Thresholds(
    MinExpectancy5m15m: minExp5m15m,
    MinExpectancy30m1h: minExp30m1h,
    MinExpectancy2h6h: minExp2h6h,
    MinAccuracy: minAccuracy,
    MinConfidence: minConfidence,
    MinTotal5m: minTotal5m,
    MinTotal15m: minTotal15m,
    MinTotal30m1h: minTotal30m1h,
    MinTotal2h6h: minTotal2h6h
);

var risk = new BotSettingsBuilder.Risk(
    RiskPctPerTrade: riskPct,
    MaxDailyLossPct: maxDailyLossPct,
    MaxOpenPositionsPerSymbol: maxOpenPerSymbol,
    CooldownMinutesPerSymbol: cooldownMinutes
);

Console.WriteLine($"Generating settings for ConfigHash={configHash}...");
var path = await BotSettingsBuilder.BuildAsync(
    dbOptions,
    configHash: configHash!,
    t: thresholds,
    r: risk,
    outputPath: outputPath
);

Console.WriteLine($"Wrote {path}");
