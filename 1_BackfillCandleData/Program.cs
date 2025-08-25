using BackfillCandleData.Services;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Shared;

namespace BackfillCandleData
{
    internal static class Program
    {
        private static async Task Main(string[] args)
        {
            var options = new DbContextOptionsBuilder<SharedDbContext>()
                .UseSqlServer("Server=localhost;Database=Tradexus;Trusted_Connection=True;Encrypt=False;")
                .Options;

            var metrics = new MetricsService();
            var price = new PriceMonitorService(Log.Logger, metrics);


            //List<string> symbols2 = new List<string>{ "XRP_USDT", "BTC_USDT", "ETH_USDT", "ADA_USDT", "HBAR_USDT", "LINK_USDT" };

            using (var db = new SharedDbContext(options))
            {
                //"XRP_USDT", "BTC_USDT", "ETH_USDT", "ADA_USDT", "HBAR_USDT", "LINK_USDT"
                //pairs we want to backfill and test data with
                await price.BackfillRangeAsync(db, new[] { "XRP_USDT", "BTC_USDT", "ETH_USDT", "ADA_USDT", "HBAR_USDT", "LINK_USDT" }, new[] { TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5) }, startUtc: new DateTime(2023, 8, 19, 0, 0, 0, DateTimeKind.Utc), endUtc: DateTime.UtcNow);
            }
        }
    }
}

