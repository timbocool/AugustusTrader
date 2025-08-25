using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RecoPaperTrader;
using System.Text.Json;
using System.Text.RegularExpressions;
using Serilog;

static string? Arg(string[] args, string key, string shortKey = "")
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

static bool IsLikelyConfigHash(string s)
{
    if (string.IsNullOrWhiteSpace(s)) return false;
    s = s.Trim();
    // support 32 or 64 hex chars (you’re using 32 right now)
    return Regex.IsMatch(s, @"\A[0-9a-fA-F]{32}([0-9a-fA-F]{32})?\z");
}

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables();

builder.Logging.ClearProviders().AddConsole();

var cfg = builder.Configuration;

var conn = Arg(args, "--conn")
    ?? Environment.GetEnvironmentVariable("TRX_CONN")
    ?? cfg.GetConnectionString("Tradexus")
    ?? cfg["ConnectionStrings:Tradexus"];

if (string.IsNullOrWhiteSpace(conn))
{
    Console.Error.WriteLine("Missing connection string. Use --conn or env TRX_CONN or appsettings.json ConnectionStrings:Tradexus.");
    return;
}

// --- Resolve settings path from (path | config-hash | prompt) ---
var defaultSettingsDir =
    Arg(args, "--settings-dir")
    ?? Environment.GetEnvironmentVariable("BOT_SETTINGS_DIR")
    ?? @"C:\Users\timco\source\repos\AugustusTrader\RecoPaperTrader\BotConfigurations";

var settingsPathArg = Arg(args, "--settings");
var configHashArg = Arg(args, "--config", "-c");

string settingsPath;

if (!string.IsNullOrWhiteSpace(settingsPathArg))
{
    settingsPath = settingsPathArg.Trim('"', ' ');
}
else if (!string.IsNullOrWhiteSpace(configHashArg))
{
    var hash = configHashArg.Trim();
    settingsPath = Path.Combine(defaultSettingsDir, $"bot.settings.{hash}.json");
}
else
{
    Console.WriteLine($"Settings directory: {defaultSettingsDir}");
    Console.Write("Paste ConfigHash (e.g. 0EF7E17F34C6C168C63F5165F680A87D) or full path to settings file: ");
    var input = (Console.ReadLine() ?? "").Trim().Trim('"');

    if (File.Exists(input))
    {
        settingsPath = input;
    }
    else if (IsLikelyConfigHash(input))
    {
        settingsPath = Path.Combine(defaultSettingsDir, $"bot.settings.{input}.json");
    }
    else
    {
        Console.Error.WriteLine("Input was neither a valid path nor a hex ConfigHash.");
        return;
    }
}

if (!File.Exists(settingsPath))
{
    Console.Error.WriteLine($"Settings file not found: {settingsPath}");
    return;
}

var settings = JsonSerializer.Deserialize<BotSettings>(
    File.ReadAllText(settingsPath),
    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

if (settings is null || settings.Strategies.Count == 0)
{
    Console.Error.WriteLine("Settings JSON invalid or empty.");
    return;
}

Console.WriteLine($"Loaded settings for ConfigHash={settings.ConfigHash} with {settings.Strategies.Count} strategy/timeframe pairs.");

var traderSection = cfg.GetSection("Trader");
var opt = traderSection.Get<TraderOptions>() ?? new TraderOptions();

// Give each config its own watermark file, unless user already set one
if (string.Equals(opt.StatePath, "paper.watermark.json", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(settings.ConfigHash))
{
    opt.StatePath = $"paper.watermark.{settings.ConfigHash}.json";
}

// CLI overrides (optional)
if (Arg(args, "--poll") is string poll && int.TryParse(poll, out var pms)) opt.PollMilliseconds = pms;
if (Arg(args, "--lookback") is string lb && int.TryParse(lb, out var lbm)) opt.LookbackMinutes = lbm;
if (Arg(args, "--account") is string acctName && !string.IsNullOrWhiteSpace(acctName)) opt.AccountName = acctName;

// EF registration (pick one)
builder.Services.AddPooledDbContextFactory<TradexusDb>(o => o.UseSqlServer(conn), poolSize: 16);
// or, without pooling:
// builder.Services.AddDbContextFactory<TradexusDb>(o => o.UseSqlServer(conn));

builder.Services.AddSingleton(opt);
builder.Services.AddSingleton(settings);
builder.Services.AddSingleton<PaperTrader>();

var app = builder.Build();
var trader = app.Services.GetRequiredService<PaperTrader>();
Console.WriteLine("Paper trader running. Press Ctrl+C to stop.");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

await trader.RunAsync(cts.Token);
