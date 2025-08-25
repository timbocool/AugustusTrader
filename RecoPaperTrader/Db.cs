using Microsoft.EntityFrameworkCore;

namespace RecoPaperTrader;

public class TradexusDb : DbContext
{
    public TradexusDb(DbContextOptions<TradexusDb> options) : base(options) { }
    public DbSet<RecoLog> RecoLog => Set<RecoLog>();
    public DbSet<Candle> Candles => Set<Candle>();
    public DbSet<PaperAccount> PaperAccount => Set<PaperAccount>();
    public DbSet<PaperPosition> PaperPosition => Set<PaperPosition>();
    public DbSet<PaperFill> PaperFill => Set<PaperFill>();
}
