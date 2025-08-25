using Microsoft.EntityFrameworkCore;
using Shared.Models;

namespace Shared
{
    public class SharedDbContext : DbContext
    {
        public SharedDbContext(DbContextOptions<SharedDbContext> options)
            : base(options)
        {
        }

        // Map the MarketData table
        public DbSet<MarketData> MarketData { get; set; } = null!;
        public DbSet<Candle> Candles { get; set; } = null!;
        public DbSet<RecoLog> RecoLog { get; set; }
        public DbSet<RecoEvaluation> RecoEvaluation => Set<RecoEvaluation>();
        public DbSet<RecoStrategyAccuracy> RecoStrategyAccuracy => Set<RecoStrategyAccuracy>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            //modelBuilder.Entity<RecoLogTest>().HasKey(x => x.Id);
            modelBuilder.Entity<RecoEvaluation>().HasKey(x => x.Id);
            modelBuilder.Entity<RecoStrategyAccuracy>().HasKey(x => x.Id);
        }
    }


}
