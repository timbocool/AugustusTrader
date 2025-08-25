//using Microsoft.EntityFrameworkCore;
//using BacktesterStandalone.Models;

//namespace BacktesterStandalone
//{
//    // read-only source for candles (points at the same SQL DB)
//    public class AnalyzerLightDbContext : DbContext
//    {
//        public AnalyzerLightDbContext(DbContextOptions<AnalyzerLightDbContext> options) : base(options) { }
//        public DbSet<Candle> Candles => Set<Candle>();
//    }

//    // destination test tables
//    public class BacktestDbContext : DbContext
//    {
//        public BacktestDbContext(DbContextOptions<BacktestDbContext> options) : base(options) { }

//        public DbSet<RecoLog> RecoLog => Set<RecoLog>();


//        protected override void OnModelCreating(ModelBuilder modelBuilder)
//        {

//        }
//    }
//}