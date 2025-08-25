using Microsoft.EntityFrameworkCore;
using Serilog;
using System.Text.Json;

namespace RecoPaperTrader;

public class TraderOptions
{
    public int PollMilliseconds { get; set; } = 3000;
    public int LookbackMinutes { get; set; } = 30;
    public string StatePath { get; set; } = "paper.watermark.json";
    public double MinConfidenceAbs { get; set; } = 0.55;
    public int CooldownMinutesPerSymbol { get; set; } = 60;
    public int EntryLagBars { get; set; } = 1;
    public int HorizonBars { get; set; } = 1;
    public decimal FeeBpsPerSide { get; set; } = 0.5m;
    public decimal SlippageBpsPerSide { get; set; } = 1.5m;
    public decimal StartingEquity { get; set; } = 100_000m;
    public string AccountName { get; set; } = "Paper-Default";
}

public class TraderState
{
    public long LastSeenRecoId { get; set; }
    public Dictionary<string, DateTime> CooldownsUtc { get; set; } = new();
}

public class PaperTrader
{
    private readonly IDbContextFactory<TradexusDb> _dbFactory;
    private readonly Serilog.ILogger _log;
    private readonly TraderOptions _opt;
    private readonly BotSettings _settings;

    private TraderState _state = new();

    public PaperTrader(IDbContextFactory<TradexusDb> dbFactory,
                       TraderOptions opt,
                       BotSettings settings)
    {
        _dbFactory = dbFactory;
        _opt = opt;
        _settings = settings;
        _log = Log.ForContext<PaperTrader>(); // Serilog logger for this class
        LoadState();
    }

    public async Task RunAsync(CancellationToken ct)
    {
        _log.Information("PaperTrader started. Poll={Poll}ms Lookback={LB}m HorizonBars={HB} EntryLagBars={EL}",
            _opt.PollMilliseconds, _opt.LookbackMinutes, _opt.HorizonBars, _opt.EntryLagBars);

        var allow = _settings.Strategies.Select(s => (s.Strategy, s.Timeframe)).ToHashSet();

        await EnsureAccountAsync(ct);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var db = await _dbFactory.CreateDbContextAsync(ct);

                // 1) settle positions whose horizon bar is now available
                await TrySettleDuePositionsAsync(db, ct);

                // 2) fetch new recos
                var sinceTs = DateTime.UtcNow.AddMinutes(-_opt.LookbackMinutes);
                var lastId = _state.LastSeenRecoId;

                var recos = await db.RecoLog
                    .Where(r => r.Id > lastId && r.Timestamp >= sinceTs)
                    .OrderBy(r => r.Id)
                    .AsNoTracking()
                    .Take(5000)
                    .ToListAsync(ct);

                if (recos.Count == 0)
                {
                    await Task.Delay(_opt.PollMilliseconds, ct);
                    continue;
                }

                foreach (var r in recos)
                {
                    _state.LastSeenRecoId = Math.Max(_state.LastSeenRecoId, r.Id);

                    if (!allow.Contains((r.Detector, r.Timeframe)))
                        continue;

                    // confidence gate: absolute or relative to modelled avg
                    var metaForPair = _settings.Strategies.Where(s => s.Strategy == r.Detector && s.Timeframe == r.Timeframe).ToList();
                    if (metaForPair.Count == 0) continue;

                    var confMin = Math.Max(_opt.MinConfidenceAbs, 0.8 * metaForPair.Max(s => s.AvgConfidence));
                    if ((double)r.Confidence < confMin)
                        continue;

                    // cooldown per symbol+timeframe
                    var key = $"{r.Symbol}|{r.Timeframe}";
                    var cooldownMins = _settings.RiskBot.CooldownMinutesPerSymbol > 0
                        ? _settings.RiskBot.CooldownMinutesPerSymbol
                        : _opt.CooldownMinutesPerSymbol;

                    if (_state.CooldownsUtc.TryGetValue(key, out var until) && until > DateTime.UtcNow)
                        continue;

                    await PlaceVirtualOrderAsync(db, r, ct);
                    _state.CooldownsUtc[key] = DateTime.UtcNow.AddMinutes(cooldownMins);
                }

                SaveState();
            }
            catch (TaskCanceledException) { }
            catch (Exception ex)
            {
                _log.Error(ex, "PaperTrader loop error.");
                await Task.Delay(_opt.PollMilliseconds, ct);
            }
        }
    }

    private async Task EnsureAccountAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var acct = await db.PaperAccount.FirstOrDefaultAsync(a => a.AccountName == _opt.AccountName, ct);
        if (acct == null)
        {
            acct = new PaperAccount
            {
                AccountName = _opt.AccountName,
                CreatedUtc = DateTime.UtcNow,
                Equity = _opt.StartingEquity,
                Cash = _opt.StartingEquity
            };
            db.PaperAccount.Add(acct);
            await db.SaveChangesAsync(ct);
            _log.Information("Initialized paper account '{Name}' with equity {Eq}", _opt.AccountName, _opt.StartingEquity);
        }
    }

    private async Task PlaceVirtualOrderAsync(TradexusDb db, RecoLog r, CancellationToken ct)
    {
        var acct = await db.PaperAccount.AsTracking().FirstAsync(a => a.AccountName == _opt.AccountName, ct);
        var tfSpan = TimeframeUtils.Parse(r.Timeframe);

        var entryBarStart = r.Timestamp + TimeSpan.FromTicks(tfSpan.Ticks * _opt.EntryLagBars);
        var horizonBars = _opt.HorizonBars;
        var exitDue = entryBarStart + TimeSpan.FromTicks(tfSpan.Ticks * horizonBars);

        // entry price proxy = reco price +/- slippage
        var slipFrac = _opt.SlippageBpsPerSide / 10_000m;
        var feeFrac = _opt.FeeBpsPerSide / 10_000m;
        var isBuy = r.Direction.Equals("BUY", StringComparison.OrdinalIgnoreCase);
        var sign = isBuy ? 1m : -1m;

        var entryPx = r.Price * (1m + (isBuy ? slipFrac : -slipFrac));
        if (entryPx <= 0) return;

        // position sizing
        var riskPct = _settings.RiskBot.RiskPctPerTrade > 0 ? _settings.RiskBot.RiskPctPerTrade : 0.0025m;
        var notional = acct.Equity * riskPct;
        if (notional <= 0) return;

        var qty = notional / entryPx; // long or short qty magnitude
        if (qty <= 0) return;

        var entryFee = notional * feeFrac;

        // cash: naive book (shorting increases cash)
        acct.Cash -= sign * notional;
        acct.Cash -= entryFee;

        db.PaperFill.Add(new PaperFill
        {
            AccountName = _opt.AccountName,
            Side = isBuy ? "BUY" : "SELL",
            Symbol = r.Symbol,
            Timeframe = r.Timeframe,
            Strategy = r.Detector,
            Quantity = qty,
            Price = entryPx,
            Fee = entryFee,
            Slippage = Math.Abs(entryPx - r.Price) * qty,
            FilledUtc = DateTime.UtcNow,
            SourceRecoId = r.Id,
            Notes = "Entry"
        });
        await db.SaveChangesAsync(ct);

        var pos = new PaperPosition
        {
            AccountName = _opt.AccountName,
            Symbol = r.Symbol,
            Timeframe = r.Timeframe,
            Strategy = r.Detector,
            Direction = isBuy ? "BUY" : "SELL",
            Quantity = qty * sign, // sign denotes long/short
            EntryPrice = entryPx,
            EntryTsUtc = DateTime.UtcNow,
            EntryBarStartTime = entryBarStart,
            HorizonBars = horizonBars,
            ExitDueBarStartTime = exitDue,
            ConfigHash = _settings.ConfigHash,
            Status = "OPEN"
        };
        db.PaperPosition.Add(pos);
        await db.SaveChangesAsync(ct);

        _log.Information("Opened {Dir} {Qty} {Sym} {TF} @ {Px} (horizon {HB} bars)",
            pos.Direction, Math.Abs(pos.Quantity), pos.Symbol, pos.Timeframe, pos.EntryPrice, horizonBars);
    }

    private async Task TrySettleDuePositionsAsync(TradexusDb db, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var open = await db.PaperPosition
            .Where(p => p.AccountName == _opt.AccountName && p.Status == "OPEN" && p.ExitDueBarStartTime <= now)
            .OrderBy(p => p.ExitDueBarStartTime)
            .ToListAsync(ct);

        if (open.Count == 0) return;

        foreach (var p in open)
        {
            var exitCandle = await db.Candles.AsNoTracking()
                .Where(c => c.Symbol == p.Symbol && c.Timeframe == p.Timeframe && c.StartTime == p.ExitDueBarStartTime)
                .FirstOrDefaultAsync(ct);

            if (exitCandle == null) continue; // wait until that bar exists

            var sign = p.Quantity >= 0 ? 1m : -1m;
            var slipFrac = _opt.SlippageBpsPerSide / 10_000m;
            var feeFrac = _opt.FeeBpsPerSide / 10_000m;

            var exitPxRaw = exitCandle.Close;
            var exitPx = exitPxRaw * (1m - (sign > 0 ? slipFrac : -slipFrac));
            var qtyAbs = Math.Abs(p.Quantity);

            var notionalExit = qtyAbs * exitPx;
            var exitFee = notionalExit * feeFrac;

            var pnl = (exitPx - p.EntryPrice) * p.Quantity - exitFee; // entry fee handled via cash

            db.PaperFill.Add(new PaperFill
            {
                AccountName = _opt.AccountName,
                PositionId = p.Id,
                Side = sign > 0 ? "SELL" : "BUY",
                Symbol = p.Symbol,
                Timeframe = p.Timeframe,
                Strategy = p.Strategy,
                Quantity = qtyAbs,
                Price = exitPx,
                Fee = exitFee,
                Slippage = Math.Abs(exitPx - exitPxRaw) * qtyAbs,
                FilledUtc = DateTime.UtcNow,
                Notes = "Exit@Horizon"
            });

            var acct = await db.PaperAccount.AsTracking().FirstAsync(a => a.AccountName == _opt.AccountName, ct);
            acct.Cash += sign * notionalExit;
            acct.Cash -= exitFee;
            acct.Equity += pnl;

            p.Status = "CLOSED";
            await db.SaveChangesAsync(ct);

            _log.Information("Closed {Sym} {TF} {Strat} qty={Qty} entry={En} exit={Ex} pnl={Pnl} eq={Eq}",
                p.Symbol, p.Timeframe, p.Strategy, qtyAbs, p.EntryPrice, exitPx, pnl, acct.Equity);
        }
    }

    private void LoadState()
    {
        try
        {
            if (File.Exists(_opt.StatePath))
                _state = JsonSerializer.Deserialize<TraderState>(File.ReadAllText(_opt.StatePath)) ?? new();
        }
        catch { _state = new(); }
    }
    private void SaveState()
    {
        try
        {
            File.WriteAllText(_opt.StatePath, JsonSerializer.Serialize(_state, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
