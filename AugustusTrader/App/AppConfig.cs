using Microsoft.EntityFrameworkCore;
using Shared;
using System;

namespace ObserverBot.App
{
    public sealed class AppConfig
    {
        public TimeSpan[] Timeframes { get; init; } = Array.Empty<TimeSpan>();
        public string[] IncludeSymbols { get; init; } = Array.Empty<string>();
        public string[] ExcludeSymbols { get; init; } = Array.Empty<string>();

        public static AppConfig Default()
        {
            string[] symbols;

            try
            {
                // Build DbContext options (replace with your real connection string)
                var options = new DbContextOptionsBuilder<SharedDbContext>()
                    .UseSqlServer("Server=localhost;Database=Tradexus;Trusted_Connection=True;Encrypt=False;")
                    .Options;

                using var db = new SharedDbContext(options);

                symbols = db.MarketData
                    .Where(m => m.State == "NORMAL"
                   /* && m.Symbol == "XRP_USDT"*/)
                    .OrderBy(m => m.RecordId)
                    .Select(m => m.Symbol)
                    .ToArray();
            }
            catch
            {
                // fallback if DB not available
                symbols = Array.Empty<string>();
            }

            return new AppConfig
            {
                Timeframes = new[]
                {
                    //TimeSpan.FromMinutes(1),
                    TimeSpan.FromMinutes(5),
                    TimeSpan.FromMinutes(15),
                    TimeSpan.FromMinutes(30),

                    TimeSpan.FromHours(1),
                    TimeSpan.FromHours(2),
                    TimeSpan.FromHours(6),
                    TimeSpan.FromHours(12),

                    TimeSpan.FromDays(1),
                    TimeSpan.FromDays(3),
                    TimeSpan.FromDays(7),
                    TimeSpan.FromDays(30)

                },

                IncludeSymbols = symbols,
                ExcludeSymbols = Array.Empty<string>()
            };
        }
    }
}
