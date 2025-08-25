using Serilog;
using Shared.Models;

namespace ObserverBot.Services
{
    public sealed class PatternEngineService
    {
        public event Action<Recommendation>? OnRecommendation;

        private readonly ILogger _log;
        private readonly MetricsService _metrics;
        public static readonly PatternEngine DefaultEngine = PatternEngine.Default();

        public PatternEngineService(ILogger log, MetricsService metrics)
        {
            _log = log;
            _metrics = metrics;
        }

        public void Emit(Recommendation r)
        {
            _metrics.IncReco();
            OnRecommendation?.Invoke(r);
        }
    }

    public interface IPatternDetector
    {
        string Name { get; }
        IEnumerable<Recommendation> Evaluate(string symbol, TimeSpan tf, IReadOnlyList<TickerBar> hist);
    }

    public sealed class PatternEngine
    {
        private readonly List<IPatternDetector> _det = new();

        public PatternEngine Add(IPatternDetector d) { _det.Add(d); return this; }

        public IEnumerable<Recommendation> OnBarClosed(string symbol, TimeSpan tf, IReadOnlyList<TickerBar> hist)
        {
            if (hist.Count < 12) yield break;
            foreach (var d in _det)
            {
                IEnumerable<Recommendation> rs = Array.Empty<Recommendation>();
                try { rs = d.Evaluate(symbol, tf, hist); } catch { }
                foreach (var r in rs) yield return r;
            }
        }

        public static PatternEngine Default() => new PatternEngine()
            //Breakout checks
            .Add(new BreakoutN(lookback: 12, pct: 0.0015m))
            .Add(new BreakoutN_Lean(lookback: 12, pct: 0.0015m))
            .Add(new BreakoutN_VolAdj(lookback: 12, atrPeriod: 14, atrMult: 0.8m, minChannelPct: 0.0005m))

            //Inside bar checks
            .Add(new InsideBarBreak())
            .Add(new InsideBarWickBreak())
            .Add(new InsideBarBreakConfirm())
            .Add(new InsideBarFakey())
            .Add(new InsideBarNR7Break())
            .Add(new InsideBarTrendBreak(emaPeriod: 20))
            .Add(new InsideBarBreakRetest(retestTolerancePct: 0.0005m))

            //Hammer reversals
            .Add(new HammerReversal())
            .Add(new HammerReversalPlus())

            //EMA Cross
            .Add(new EmaCross(9, 21))
            // --- EMA cross variants ---
            .Add(new EmaCrossSlope(fast: 9, slow: 21, slopeLookback: 3))          // Cross + EMA slopes aligned
            .Add(new EmaCrossPriceSide(fast: 9, slow: 21))                         // Cross + close above/below both EMAs
            .Add(new EmaCrossConfirm(fast: 9, slow: 21))                           // Cross on c, still valid on d
            .Add(new EmaCrossWide(fast: 9, slow: 21, minSeparationPct: 0.0005m))   // Cross with minimum EMA separation

            //.Add(new EmaCrossTrendBias(fast: 9, slow: 21, longEma: 200))           // Cross, biased by long EMA trend

            //RSI
            .Add(new RsiExtreme(14, low: 32m, high: 68m))
            // --- RSI variants ---
            .Add(new RsiExtremeWilder(len: 14, low: 30m, high: 70m))          // Wilder-smoothed RSI extremes
            .Add(new RsiCrossThreshold(len: 14, low: 30m, high: 70m))          // Cross back through bands
            .Add(new RsiRegime50(len: 14, mid: 50m, minDelta: 0.5m))           // 50-line regime cross
            .Add(new RsiFailureSwingLite(len: 14, low: 30m, high: 70m))        // Simple failure swing
            .Add(new RsiDivergence(len: 14, lookback: 20))                     // Price/RSI divergence
            .Add(new RsiAdaptiveBands(len: 14, bandLookback: 100, lowPct: 0.20m, highPct: 0.80m)) // Adaptive bands

            // --- Bollinger/Keltner squeeze variants ---
            .Add(new BollSqueezeBreakout(20, widthPct: 0.012m, breakPct: 0.003m))
            .Add(new BollSqueezeBandBreak(period: 20, widthPct: 0.012m, bandBreakPct: 0.000m))            // Break the band itself
            .Add(new BollSqueezeConfirm(period: 20, widthPct: 0.012m, breakPct: 0.003m))                   // Two-bar confirmation
            .Add(new BollInsideKeltnerBreakout(period: 20, kcMult: 1.5m, bbMult: 2m, breakPct: 0.003m))    // BB inside KC (TTM-style)
            .Add(new BollSqueezePercentile(period: 20, widthLookback: 120, lowPercentile: 0.15m, breakPct: 0.003m)) // Adaptive width
            .Add(new BollSqueezeRetest(period: 20, widthPct: 0.012m, retestTolPct: 0.0005m))             // Break then retest

            // --- VWAP variants ---
            .Add(new VwapDrift(0.003m, 0.003m))
            .Add(new VwapDriftPlus(up: 0.003m, down: 0.003m, volLookback: 20, minVolMult: 0.5m, minRangePct: 0.0003m))
            .Add(new VwapCross(minDrift: 0.0003m))
            .Add(new VwapZScoreExtreme(n: 30, zUp: 2.0m, zDown: 2.0m))
            .Add(new VwapReversionCrossback(threshold: 0.004m))
            .Add(new VwapTrendPullback(emaPeriod: 50, touchTolerance: 0.0005m))
            .Add(new VwapMultiWindowAlign(shortBars: 20, midBars: 60))

            // === New detectors below ===
            .Add(new DonchianBreakout(20))
            .Add(new AtrBreakout(14, 1.5m))
            .Add(new MacdCross(12, 26, 9))
            .Add(new StochasticCross(14, 3, 20m, 80m))
            .Add(new Engulfing())
            .Add(new DojiReversal())
            .Add(new Nr7Breakout())
            .Add(new KeltnerSqueezeBreakout(20, 2m, 0.003m))
            .Add(new PullbackToEma(20, 0.001m))
            .Add(new ObvBreakout(lookback: 50))
            .Add(new VolumeSpike(20, 2.5m))
            .Add(new FractalBreakout())
            .Add(new ThreeSoldiersCrows())
            .Add(new MorningEveningStar())
            .Add(new TweezerTopBottom())
            .Add(new BollingerReversion(20, 2m, 0.002m))
            .Add(new AdxDiCross(14, 20m));
    }

    // Simple N-bar channel breakout detector.
    // Idea: if the *latest closed bar's close* exceeds the highest high (or lowest low)
    // of the previous N bars by a small buffer, emit a BUY (or SELL) recommendation.
    public sealed class BreakoutN : IPatternDetector
    {
        public string Name => "BreakoutN";

        private readonly int _lookback;
        private readonly decimal _pct;

        public BreakoutN(int lookback = 12, decimal pct = 0.0015m)
        {
            _lookback = lookback;
            _pct = pct;
        }

        public IEnumerable<Recommendation> Evaluate(string symbol, TimeSpan tf, IReadOnlyList<TickerBar> hist)
        {
            if (hist.Count < _lookback + 2)
                yield break;

            var last = hist[^1];

            var prev = hist.Skip(hist.Count - _lookback - 1)
                           .Take(_lookback)
                           .ToList();

            var maxH = prev.Max(b => b.High);
            var minL = prev.Min(b => b.Low);

            if (last.Close >= maxH * (1 + _pct))
            {
                yield return new Recommendation(
                    symbol,
                    tf,
                    Name,
                    "BUY",
                    ComputeConfidence(last, prev, maxH, isBuy: true),
                    last.Close,
                    $"break>{_lookback}H"
                );
            }
            else if (last.Close <= minL * (1 - _pct))
            {
                yield return new Recommendation(
                    symbol,
                    tf,
                    Name,
                    "SELL",
                    ComputeConfidence(last, prev, minL, isBuy: false),
                    last.Close,
                    $"break<{_lookback}L"
                );
            }
        }

        private decimal ComputeConfidence(TickerBar last, IReadOnlyList<TickerBar> prev, decimal breakoutLevel, bool isBuy)
        {
            // ATR-like average range
            var atr = prev.Average(b => b.High - b.Low);
            var dist = Math.Abs(last.Close - breakoutLevel);
            var strength = dist / (atr + 1e-9m);

            // Base confidence from distance: 0.45 → 0.90
            var conf = 0.45m + Math.Min(strength, 3) * 0.15m;

            // Volume factor: reward higher volume
            var avgVol = prev.Average(b => b.Volume);
            var volFactor = (decimal)last.Volume / (decimal)(avgVol + 1e-9m);
            volFactor = Math.Clamp(volFactor, 0.5m, 2.0m); // bound
            conf *= (0.8m + 0.2m * volFactor);             // 0.8–1.2x adjustment

            // Candle body vs wick
            var bodySize = Math.Abs(last.Close - last.Open);
            var fullRange = last.High - last.Low + 1e-9m;
            var bodyPct = bodySize / fullRange;
            conf *= (0.85m + 0.15m * bodyPct);             // 0.85–1.0x adjustment

            return Math.Clamp(conf, 0.3m, 0.97m);
        }
    }

    public sealed class BreakoutN_Lean : IPatternDetector
    {
        public string Name => "BreakoutN";

        private readonly int _lookback;
        private readonly decimal _pct;
        private readonly decimal _minChannelPct;

        public BreakoutN_Lean(int lookback = 12, decimal pct = 0.0015m, decimal minChannelPct = 0.0005m)
        {
            _lookback = lookback;
            _pct = pct;
            _minChannelPct = minChannelPct;
        }

        public IEnumerable<Recommendation> Evaluate(string symbol, TimeSpan tf, IReadOnlyList<TickerBar> hist)
        {
            if (hist.Count < _lookback + 2)
                yield break;

            int end = hist.Count - 2;
            int start = end - _lookback + 1;

            decimal maxH = hist[start].High;
            decimal minL = hist[start].Low;

            for (int i = start + 1; i <= end; i++)
            {
                var h = hist[i].High;
                var l = hist[i].Low;
                if (h > maxH) maxH = h;
                if (l < minL) minL = l;
            }

            var last = hist[^1];

            // Guard: suppress signals if the channel is too tight
            var refPrice = last.Close == 0 ? 1 : last.Close;
            var channelPct = (maxH - minL) / refPrice;
            if (channelPct < _minChannelPct)
                yield break;

            var prevClose = hist[^2].Close;
            bool alreadyUp = prevClose >= maxH * (1 + _pct);
            bool alreadyDn = prevClose <= minL * (1 - _pct);

            if (!alreadyUp && last.Close >= maxH * (1 + _pct))
            {
                yield return new Recommendation(
                    symbol, tf, Name, "BUY",
                    ComputeConfidence(last, hist, start, end, maxH, isBuy: true),
                    last.Close,
                    $"break>{_lookback}H w={channelPct:P2}"
                );
            }
            else if (!alreadyDn && last.Close <= minL * (1 - _pct))
            {
                yield return new Recommendation(
                    symbol, tf, Name, "SELL",
                    ComputeConfidence(last, hist, start, end, minL, isBuy: false),
                    last.Close,
                    $"break<{_lookback}L w={channelPct:P2}"
                );
            }
        }

        private decimal ComputeConfidence(
            TickerBar last,
            IReadOnlyList<TickerBar> hist,
            int start,
            int end,
            decimal breakoutLevel,
            bool isBuy)
        {
            // ATR-like measure across the channel window
            decimal atrSum = 0m;
            for (int i = start; i <= end; i++)
                atrSum += hist[i].High - hist[i].Low;
            var atr = atrSum / (end - start + 1);

            var dist = Math.Abs(last.Close - breakoutLevel);
            var strength = dist / (atr + 1e-9m);

            var conf = 0.45m + Math.Min(strength, 3) * 0.15m; // 0.45–0.90

            // Volume adjustment vs average
            decimal volSum = 0m;
            for (int i = start; i <= end; i++)
                volSum += hist[i].Volume;
            var avgVol = volSum / (end - start + 1);

            var volFactor = (decimal)last.Volume / (decimal)(avgVol + 1e-9m);
            volFactor = Math.Clamp(volFactor, 0.5m, 2.0m);
            conf *= (0.8m + 0.2m * volFactor);

            // Candle body strength
            var bodySize = Math.Abs(last.Close - last.Open);
            var fullRange = last.High - last.Low + 1e-9m;
            var bodyPct = bodySize / fullRange;
            conf *= (0.85m + 0.15m * bodyPct);

            return Math.Clamp(conf, 0.3m, 0.97m);
        }
    }

    public sealed class BreakoutN_VolAdj : IPatternDetector
    {
        public string Name => "BreakoutN-ATR";

        private readonly int _lookback;
        private readonly int _atrPeriod;
        private readonly decimal _atrMult;
        private readonly decimal _minChannelPct;

        public BreakoutN_VolAdj(int lookback = 12, int atrPeriod = 14, decimal atrMult = 0.8m, decimal minChannelPct = 0.0005m)
        {
            _lookback = lookback;
            _atrPeriod = atrPeriod;
            _atrMult = atrMult;
            _minChannelPct = minChannelPct;
        }

        public IEnumerable<Recommendation> Evaluate(string symbol, TimeSpan tf, IReadOnlyList<TickerBar> hist)
        {
            int need = Math.Max(_lookback + 2, _atrPeriod + 2);
            if (hist.Count < need)
                yield break;

            int end = hist.Count - 2;
            int start = end - _lookback + 1;

            decimal maxH = hist[start].High;
            decimal minL = hist[start].Low;
            for (int i = start + 1; i <= end; i++)
            {
                var h = hist[i].High;
                var l = hist[i].Low;
                if (h > maxH) maxH = h;
                if (l < minL) minL = l;
            }

            var last = hist[^1];
            var refPrice = last.Close == 0 ? 1 : last.Close;
            var channelPct = (maxH - minL) / refPrice;
            if (channelPct < _minChannelPct)
                yield break;

            // ATR over last _atrPeriod bars (ending at ^1)
            decimal atr = 0m;
            for (int i = hist.Count - _atrPeriod; i < hist.Count; i++)
            {
                var t = hist[i];
                var p = hist[i - 1];
                var tr = Math.Max((double)(t.High - t.Low),
                          Math.Max((double)Math.Abs(t.High - p.Close),
                                   (double)Math.Abs(t.Low - p.Close)));
                atr += (decimal)tr;
            }
            atr /= _atrPeriod;
            if (atr <= 0) yield break;

            var upLvl = maxH + _atrMult * atr;
            var dnLvl = minL - _atrMult * atr;

            var prevClose = hist[^2].Close;
            bool alreadyUp = prevClose >= upLvl;
            bool alreadyDn = prevClose <= dnLvl;

            if (!alreadyUp && last.Close >= upLvl)
            {
                var conf = ComputeConfidence(last, hist, start, end, upLvl, atr, isBuy: true);
                yield return new Recommendation(symbol, tf, Name, "BUY", conf, last.Close,
                    $"break>{_lookback}H atr={atr:F5}");
            }
            else if (!alreadyDn && last.Close <= dnLvl)
            {
                var conf = ComputeConfidence(last, hist, start, end, dnLvl, atr, isBuy: false);
                yield return new Recommendation(symbol, tf, Name, "SELL", conf, last.Close,
                    $"break<{_lookback}L atr={atr:F5}");
            }
        }

        private decimal ComputeConfidence(
            TickerBar last,
            IReadOnlyList<TickerBar> hist,
            int start,
            int end,
            decimal breakoutLevel,
            decimal atr,
            bool isBuy)
        {
            // --- Breakout strength ---
            var dist = Math.Abs(last.Close - breakoutLevel);
            var strength = dist / (atr + 1e-9m);
            var conf = 0.45m + Math.Min(strength, 3) * 0.15m; // 0.45–0.90

            // --- Volume factor ---
            decimal volSum = 0m;
            for (int i = start; i <= end; i++)
                volSum += hist[i].Volume;
            var avgVol = volSum / (end - start + 1);

            var volFactor = (decimal)last.Volume / (decimal)(avgVol + 1e-9m);
            volFactor = Math.Clamp(volFactor, 0.5m, 2.0m);
            conf *= (0.8m + 0.2m * volFactor);

            // --- Candle body strength ---
            var bodySize = Math.Abs(last.Close - last.Open);
            var fullRange = last.High - last.Low + 1e-9m;
            var bodyPct = bodySize / fullRange;
            conf *= (0.85m + 0.15m * bodyPct);

            return Math.Clamp(conf, 0.3m, 0.97m);
        }
    }



    // Detects the classic "inside bar" setup and triggers on a breakout of the
    // mother bar's range by the *next* closed bar.
    //
    // Pattern (using the last three CLOSED bars):
    //   a = hist[^3]  -> "mother" bar (defines the range to break)
    //   b = hist[^2]  -> "inside" bar (must be fully contained within 'a')
    //   c = hist[^1]  -> "breakout" bar (must CLOSE beyond 'a' to signal)
    //
    // Notes:
    // - We purposely use CLOSE comparisons for 'c' to reduce wick-only fakeouts.
    // - Equality in the "inside" test is allowed (<=/>=). Breakout uses strict
    //   >/< so a close exactly on a.High/a.Low does NOT fire.
    public sealed class InsideBarBreak : IPatternDetector
    {
        public string Name => "InsideBarBreak";

        public IEnumerable<Recommendation> Evaluate(string symbol, TimeSpan tf, IReadOnlyList<TickerBar> hist)
        {
            if (hist.Count < 3) yield break;

            var a = hist[^3]; // "mother" bar
            var b = hist[^2]; // "inside" bar
            var c = hist[^1]; // breakout bar (we evaluate this one)

            // Confirm inside bar setup
            var inside = b.High <= a.High && b.Low >= a.Low;
            if (!inside) yield break;

            if (c.Close > a.High)
            {
                yield return new Recommendation(
                    symbol,
                    tf,
                    Name,
                    "BUY",
                    ComputeConfidence(c, hist, a.High, a, b, isBuy: true),
                    c.Close,
                    "inside->up"
                );
            }
            else if (c.Close < a.Low)
            {
                yield return new Recommendation(
                    symbol,
                    tf,
                    Name,
                    "SELL",
                    ComputeConfidence(c, hist, a.Low, a, b, isBuy: false),
                    c.Close,
                    "inside->down"
                );
            }
        }

        private decimal ComputeConfidence(
            TickerBar breakout,
            IReadOnlyList<TickerBar> hist,
            decimal breakoutLevel,
            TickerBar mother,
            TickerBar inside,
            bool isBuy)
        {
            // --- Distance vs mother bar range ---
            var range = mother.High - mother.Low;
            var dist = Math.Abs(breakout.Close - breakoutLevel);
            var strength = dist / (range + 1e-9m);

            // Base confidence: 0.45–0.85
            var conf = 0.45m + Math.Min(strength, 2.5m) * 0.16m;

            // --- Volume confirmation (breakout bar vs mother+inside avg) ---
            var avgVol = (mother.Volume + inside.Volume) / 2m;
            var volFactor = (decimal)breakout.Volume / (avgVol + 1e-9m);
            volFactor = Math.Clamp(volFactor, 0.5m, 2.0m);
            conf *= (0.8m + 0.2m * volFactor);

            // --- Candle body quality (breakout bar only) ---
            var bodySize = Math.Abs(breakout.Close - breakout.Open);
            var fullRange = breakout.High - breakout.Low + 1e-9m;
            var bodyPct = bodySize / fullRange;
            conf *= (0.85m + 0.15m * bodyPct);

            return Math.Clamp(conf, 0.3m, 0.97m);
        }
    }
    // Variant: trigger as soon as the breakout bar's HIGH/LOW pierces the mother bar.
    // Earlier entries vs. your close-based version; expect more false breaks.
    public sealed class InsideBarWickBreak : IPatternDetector
    {
        public string Name => "InsideBarWickBreak";

        public IEnumerable<Recommendation> Evaluate(string symbol, TimeSpan tf, IReadOnlyList<TickerBar> hist)
        {
            if (hist.Count < 3) yield break;

            var a = hist[^3]; // mother
            var b = hist[^2]; // inside
            var c = hist[^1]; // breakout attempt

            // Confirm inside bar setup
            if (!(b.High <= a.High && b.Low >= a.Low)) yield break;

            if (c.High > a.High)
            {
                yield return new Recommendation(
                    symbol,
                    tf,
                    Name,
                    "BUY",
                    ComputeConfidence(c, a, b, a.High, isBuy: true),
                    c.Close,
                    "inside->up(wick)"
                );
            }

            if (c.Low < a.Low)
            {
                yield return new Recommendation(
                    symbol,
                    tf,
                    Name,
                    "SELL",
                    ComputeConfidence(c, a, b, a.Low, isBuy: false),
                    c.Close,
                    "inside->down(wick)"
                );
            }
        }

        private decimal ComputeConfidence(
            TickerBar breakout,
            TickerBar mother,
            TickerBar inside,
            decimal breakoutLevel,
            bool isBuy)
        {
            // --- Wick penetration depth ---
            decimal penetration;
            if (isBuy)
                penetration = breakout.High - mother.High;
            else
                penetration = mother.Low - breakout.Low;

            var range = mother.High - mother.Low;
            var strength = penetration / (range + 1e-9m);

            // Base confidence: weaker than close-break (0.40–0.75 instead of 0.45–0.90)
            var conf = 0.40m + Math.Min(strength, 2.5m) * 0.14m;

            // --- Volume factor (breakout vs mother+inside avg) ---
            var avgVol = (mother.Volume + inside.Volume) / 2m;
            var volFactor = (decimal)breakout.Volume / (avgVol + 1e-9m);
            volFactor = Math.Clamp(volFactor, 0.5m, 2.0m);
            conf *= (0.8m + 0.2m * volFactor);

            // --- Candle body quality ---
            var bodySize = Math.Abs(breakout.Close - breakout.Open);
            var fullRange = breakout.High - breakout.Low + 1e-9m;
            var bodyPct = bodySize / fullRange;

            // For wick breaks, a smaller body is actually *expected* (long wick is the signal),
            // so we down-weight this factor slightly.
            conf *= (0.9m + 0.1m * bodyPct);

            return Math.Clamp(conf, 0.25m, 0.9m);
        }
    }

    // Variant: require TWO consecutive closes beyond the mother bar.
    // Bar layout: a=mother, b=inside, c=first break close, d=confirmation close (signal fires on d).
    public sealed class InsideBarBreakConfirm : IPatternDetector
    {
        public string Name => "InsideBarBreakConfirm";

        public IEnumerable<Recommendation> Evaluate(string symbol, TimeSpan tf, IReadOnlyList<TickerBar> hist)
        {
            if (hist.Count < 4) yield break;

            var a = hist[^4]; // mother
            var b = hist[^3]; // inside
            var c = hist[^2]; // initial break close
            var d = hist[^1]; // confirmation close

            if (!(b.High <= a.High && b.Low >= a.Low)) yield break;

            if (c.Close > a.High && d.Close > a.High)
            {
                yield return new Recommendation(
                    symbol,
                    tf,
                    Name,
                    "BUY",
                    ComputeConfidence(c, d, a, b, a.High, isBuy: true),
                    d.Close,
                    "inside->up(confirm)"
                );
            }

            if (c.Close < a.Low && d.Close < a.Low)
            {
                yield return new Recommendation(
                    symbol,
                    tf,
                    Name,
                    "SELL",
                    ComputeConfidence(c, d, a, b, a.Low, isBuy: false),
                    d.Close,
                    "inside->down(confirm)"
                );
            }
        }

        private decimal ComputeConfidence(
            TickerBar firstBreak,
            TickerBar confirm,
            TickerBar mother,
            TickerBar inside,
            decimal breakoutLevel,
            bool isBuy)
        {
            // --- Distance beyond level (use confirmation bar close) ---
            var range = mother.High - mother.Low;
            var dist = Math.Abs(confirm.Close - breakoutLevel);
            var strength = dist / (range + 1e-9m);

            // Start higher than single-bar break: 0.50–0.92
            var conf = 0.50m + Math.Min(strength, 3) * 0.14m;

            // --- Volume: use average of break + confirm vs mother+inside ---
            var avgBaseVol = (mother.Volume + inside.Volume) / 2m;
            var avgBreakVol = (firstBreak.Volume + confirm.Volume) / 2m;

            var volFactor = avgBreakVol / (avgBaseVol + 1e-9m);
            volFactor = Math.Clamp(volFactor, 0.5m, 2.0m);
            conf *= (0.85m + 0.15m * volFactor);

            // --- Candle body quality (confirmation bar only) ---
            var bodySize = Math.Abs(confirm.Close - confirm.Open);
            var fullRange = confirm.High - confirm.Low + 1e-9m;
            var bodyPct = bodySize / fullRange;
            conf *= (0.9m + 0.1m * bodyPct);

            return Math.Clamp(conf, 0.35m, 0.97m);
        }
    }

    // Variant: fake breakout THROUGH one side of the mother bar that FAILS (closes back inside),
    // then the next bar breaks the opposite side. We enter in the reversal direction.
    public sealed class InsideBarFakey : IPatternDetector
    {
        public string Name => "InsideBarFakey";

        public IEnumerable<Recommendation> Evaluate(string symbol, TimeSpan tf, IReadOnlyList<TickerBar> hist)
        {
            if (hist.Count < 4) yield break;

            var a = hist[^4]; // mother
            var b = hist[^3]; // inside
            var c = hist[^2]; // false break bar
            var d = hist[^1]; // reversal bar

            if (!(b.High <= a.High && b.Low >= a.Low)) yield break;

            // False UP break -> DOWN reversal
            bool falseUp = c.High > a.High && c.Close <= a.High && c.Close >= a.Low;
            if (falseUp && d.Close < a.Low)
            {
                yield return new Recommendation(
                    symbol,
                    tf,
                    Name,
                    "SELL",
                    ComputeConfidence(c, d, a, b, breakoutUp: false),
                    d.Close,
                    "fakey->down"
                );
            }

            // False DOWN break -> UP reversal
            bool falseDn = c.Low < a.Low && c.Close >= a.Low && c.Close <= a.High;
            if (falseDn && d.Close > a.High)
            {
                yield return new Recommendation(
                    symbol,
                    tf,
                    Name,
                    "BUY",
                    ComputeConfidence(c, d, a, b, breakoutUp: true),
                    d.Close,
                    "fakey->up"
                );
            }
        }

        private decimal ComputeConfidence(
            TickerBar falseBreak,
            TickerBar reversal,
            TickerBar mother,
            TickerBar inside,
            bool breakoutUp)
        {
            var range = mother.High - mother.Low;

            // --- Conviction of the false break (penetration depth) ---
            decimal penetration;
            if (breakoutUp)
                penetration = falseBreak.High - mother.High;
            else
                penetration = mother.Low - falseBreak.Low;
            penetration = Math.Max(0, penetration);

            var fakeStrength = penetration / (range + 1e-9m);

            // --- Reversal strength (confirmation close beyond opposite boundary) ---
            decimal reversalDist;
            if (breakoutUp) // fakey->up means falseDn
                reversalDist = reversal.Close - mother.High;
            else
                reversalDist = mother.Low - reversal.Close;
            reversalDist = Math.Max(0, reversalDist);

            var reversalStrength = reversalDist / (range + 1e-9m);

            // Base confidence: start higher because fakeys are strong reversal signals
            var conf = 0.50m
                     + Math.Min(fakeStrength, 2.5m) * 0.10m
                     + Math.Min(reversalStrength, 2.5m) * 0.12m;

            // --- Volume factor (reversal bar vs mother+inside+false break) ---
            var avgBaseVol = (mother.Volume + inside.Volume + falseBreak.Volume) / 3m;
            var volFactor = (decimal)reversal.Volume / (avgBaseVol + 1e-9m);
            volFactor = Math.Clamp(volFactor, 0.5m, 2.0m);
            conf *= (0.85m + 0.15m * volFactor);

            // --- Reversal candle quality ---
            var bodySize = Math.Abs(reversal.Close - reversal.Open);
            var fullRange = reversal.High - reversal.Low + 1e-9m;
            var bodyPct = bodySize / fullRange;
            conf *= (0.9m + 0.1m * bodyPct);

            return Math.Clamp(conf, 0.35m, 0.97m);
        }
    }

    // Variant: only take inside-bar breaks when the MOTHER bar is the narrowest range of the last 7 bars (NR7).
    // Helps avoid spammy signals; favors genuine range contraction before expansion.
    public sealed class InsideBarNR7Break : IPatternDetector
    {
        public string Name => "InsideBarNR7Break";

        public IEnumerable<Recommendation> Evaluate(string symbol, TimeSpan tf, IReadOnlyList<TickerBar> hist)
        {
            if (hist.Count < 9) yield break;

            int idxA = hist.Count - 3;
            var a = hist[idxA];     // mother bar
            var b = hist[idxA + 1]; // inside
            var c = hist[idxA + 2]; // breakout

            if (!(b.High <= a.High && b.Low >= a.Low)) yield break;

            // --- NR7 test ---
            decimal aRange = a.High - a.Low;
            decimal minRange = aRange;
            for (int i = idxA - 6; i <= idxA; i++)
            {
                var r = hist[i].High - hist[i].Low;
                if (r < minRange) minRange = r;
            }
            if (aRange > minRange) yield break;

            if (c.Close > a.High)
            {
                yield return new Recommendation(
                    symbol,
                    tf,
                    Name,
                    "BUY",
                    ComputeConfidence(a, b, c, a.High, isBuy: true),
                    c.Close,
                    "inside+NR7->up"
                );
            }

            if (c.Close < a.Low)
            {
                yield return new Recommendation(
                    symbol,
                    tf,
                    Name,
                    "SELL",
                    ComputeConfidence(a, b, c, a.Low, isBuy: false),
                    c.Close,
                    "inside+NR7->down"
                );
            }
        }

        private decimal ComputeConfidence(
            TickerBar mother,
            TickerBar inside,
            TickerBar breakout,
            decimal breakoutLevel,
            bool isBuy)
        {
            var range = mother.High - mother.Low;
            var dist = Math.Abs(breakout.Close - breakoutLevel);
            var strength = dist / (range + 1e-9m);

            // Base higher than vanilla inside-bar (compression adds power)
            var conf = 0.50m + Math.Min(strength, 3m) * 0.17m; // up to ~0.99 before factors

            // --- Volume factor (breakout vs mother+inside avg) ---
            var avgVol = (mother.Volume + inside.Volume) / 2m;
            var volFactor = (decimal)breakout.Volume / (avgVol + 1e-9m);
            volFactor = Math.Clamp(volFactor, 0.5m, 2.0m);
            conf *= (0.85m + 0.15m * volFactor);

            // --- Candle body quality ---
            var bodySize = Math.Abs(breakout.Close - breakout.Open);
            var fullRange = breakout.High - breakout.Low + 1e-9m;
            var bodyPct = bodySize / fullRange;
            conf *= (0.9m + 0.1m * bodyPct);

            return Math.Clamp(conf, 0.35m, 0.97m);
        }
    }

    // Variant: only take upside breaks when price is above a rising EMA, and downside breaks when below a falling EMA.
    // Cuts counter-trend signals. Uses a light EMA computed on closes.
    public sealed class InsideBarTrendBreak : IPatternDetector
    {
        public string Name => "InsideBarTrendBreak";
        private readonly int _ema;

        public InsideBarTrendBreak(int emaPeriod = 20) { _ema = emaPeriod; }

        public IEnumerable<Recommendation> Evaluate(string symbol, TimeSpan tf, IReadOnlyList<TickerBar> hist)
        {
            if (hist.Count < Math.Max(3, _ema + 2)) yield break;

            var a = hist[^3]; // mother
            var b = hist[^2]; // inside
            var c = hist[^1]; // breakout

            if (!(b.High <= a.High && b.Low >= a.Low)) yield break;

            // --- EMA computation ---
            decimal E(IReadOnlyList<decimal> xs, int p)
            {
                var k = 2m / (p + 1);
                decimal e = xs[0];
                for (int i = 1; i < xs.Count; i++)
                    e = xs[i] * k + e * (1 - k);
                return e;
            }

            var closes = hist.Select(h => h.Close).ToList();
            var emaNow = E(closes, _ema);
            var emaPrev = E(closes.Take(closes.Count - 1).ToList(), _ema);

            bool emaUp = emaNow > emaPrev;
            bool emaDown = emaNow < emaPrev;

            if (c.Close > a.High && c.Close > emaNow && emaUp)
            {
                yield return new Recommendation(
                    symbol,
                    tf,
                    Name,
                    "BUY",
                    ComputeConfidence(a, b, c, emaNow, isBuy: true),
                    c.Close,
                    $"inside->up ema{_ema}↑"
                );
            }

            if (c.Close < a.Low && c.Close < emaNow && emaDown)
            {
                yield return new Recommendation(
                    symbol,
                    tf,
                    Name,
                    "SELL",
                    ComputeConfidence(a, b, c, emaNow, isBuy: false),
                    c.Close,
                    $"inside->down ema{_ema}↓"
                );
            }
        }

        private decimal ComputeConfidence(
            TickerBar mother,
            TickerBar inside,
            TickerBar breakout,
            decimal emaNow,
            bool isBuy)
        {
            var range = mother.High - mother.Low;
            var dist = isBuy ? breakout.Close - mother.High : mother.Low - breakout.Close;
            var strength = dist / (range + 1e-9m);

            // --- Base confidence (trend filter adds weight) ---
            var conf = 0.55m + Math.Min(strength, 3m) * 0.15m; // up to ~1.0 before factors

            // --- EMA alignment bonus (closer to EMA = weaker, farther but aligned = stronger) ---
            var emaDist = Math.Abs(breakout.Close - emaNow) / (range + 1e-9m);
            conf *= (0.9m + 0.1m * Math.Min(emaDist, 2m));

            // --- Volume factor (breakout vs mother+inside avg) ---
            var avgVol = (mother.Volume + inside.Volume) / 2m;
            var volFactor = (decimal)breakout.Volume / (avgVol + 1e-9m);
            volFactor = Math.Clamp(volFactor, 0.5m, 2.0m);
            conf *= (0.85m + 0.15m * volFactor);

            // --- Candle body quality ---
            var bodySize = Math.Abs(breakout.Close - breakout.Open);
            var fullRange = breakout.High - breakout.Low + 1e-9m;
            var bodyPct = bodySize / fullRange;
            conf *= (0.9m + 0.1m * bodyPct);

            return Math.Clamp(conf, 0.4m, 0.97m);
        }
    }

    // Variant: c breaks and CLOSES beyond the mother; d RETESTS the mother's boundary and closes in the breakout direction.
    // Often cleaner entries with tighter stops. Fires on 'd'.
    public sealed class InsideBarBreakRetest : IPatternDetector
    {
        public string Name => "InsideBarBreakRetest";
        private readonly decimal _retestTol;

        public InsideBarBreakRetest(decimal retestTolerancePct = 0.0005m)
        {
            _retestTol = retestTolerancePct;
        }

        public IEnumerable<Recommendation> Evaluate(string symbol, TimeSpan tf, IReadOnlyList<TickerBar> hist)
        {
            if (hist.Count < 4) yield break;

            var a = hist[^4]; // mother
            var b = hist[^3]; // inside
            var c = hist[^2]; // breakout
            var d = hist[^1]; // retest confirmation

            if (!(b.High <= a.High && b.Low >= a.Low)) yield break;

            if (c.Close > a.High)
            {
                var tol = a.High * _retestTol;
                bool retested = d.Low <= a.High + tol && d.Low >= a.High - tol;
                if (retested && d.Close > c.Close)
                {
                    yield return new Recommendation(
                        symbol,
                        tf,
                        Name,
                        "BUY",
                        ComputeConfidence(a, b, c, d, a.High, isBuy: true),
                        d.Close,
                        "inside->up(retest)"
                    );
                }
            }

            if (c.Close < a.Low)
            {
                var tol = a.Low * _retestTol;
                bool retested = d.High >= a.Low - tol && d.High <= a.Low + tol;
                if (retested && d.Close < c.Close)
                {
                    yield return new Recommendation(
                        symbol,
                        tf,
                        Name,
                        "SELL",
                        ComputeConfidence(a, b, c, d, a.Low, isBuy: false),
                        d.Close,
                        "inside->down(retest)"
                    );
                }
            }
        }

        private decimal ComputeConfidence(
            TickerBar mother,
            TickerBar inside,
            TickerBar breakout,
            TickerBar retest,
            decimal level,
            bool isBuy)
        {
            var range = mother.High - mother.Low;

            // --- Breakout strength (c vs mother boundary) ---
            var breakoutDist = isBuy ? breakout.Close - mother.High : mother.Low - breakout.Close;
            breakoutDist = Math.Max(0, breakoutDist);
            var breakoutStrength = breakoutDist / (range + 1e-9m);

            // --- Retest quality (d’s dip into boundary, then recovery/continuation) ---
            decimal retestDepth;
            if (isBuy)
                retestDepth = Math.Max(0, mother.High - retest.Low);
            else
                retestDepth = Math.Max(0, retest.High - mother.Low);

            var retestStrength = 1m - (retestDepth / (range + 1e-9m)); // closer to boundary = better
            retestStrength = Math.Clamp(retestStrength, 0, 1);

            // --- Base confidence: retest adds reliability ---
            var conf = 0.55m
                     + Math.Min(breakoutStrength, 2.5m) * 0.12m
                     + retestStrength * 0.15m;

            // --- Volume factor (retest vs mother+inside+breakout avg) ---
            var avgBaseVol = (mother.Volume + inside.Volume + breakout.Volume) / 3m;
            var volFactor = (decimal)retest.Volume / (avgBaseVol + 1e-9m);
            volFactor = Math.Clamp(volFactor, 0.5m, 2.0m);
            conf *= (0.85m + 0.15m * volFactor);

            // --- Candle body quality (retest bar) ---
            var bodySize = Math.Abs(retest.Close - retest.Open);
            var fullRange = retest.High - retest.Low + 1e-9m;
            var bodyPct = bodySize / fullRange;
            conf *= (0.9m + 0.1m * bodyPct);

            return Math.Clamp(conf, 0.45m, 0.97m);
        }
    }





    // Detects single-candle reversal hints using hammer / shooting star anatomy.
    // Emits one of:
    //   - "hammer"        -> BUY bias (long lower shadow, small body near top of range)
    //   - "shooting-star" -> SELL bias (long upper shadow, small body near bottom of range)
    //
    // This implementation is deliberately minimal: it looks only at the last CLOSED bar
    // and does not require a prior trend or confirmation. Expect more signals (and some noise).
    public sealed class HammerReversal : IPatternDetector
    {
        public string Name => "HammerReversal";

        public IEnumerable<Recommendation> Evaluate(string symbol, TimeSpan tf, IReadOnlyList<TickerBar> hist)
        {
            if (hist.Count < 2) yield break;

            var b = hist[^1];

            var body = Math.Abs(b.Close - b.Open);
            var range = b.High - b.Low;
            if (range <= 0) yield break;

            var lowerTail = Math.Min(b.Open, b.Close) - b.Low;
            var upperTail = b.High - Math.Max(b.Open, b.Close);

            var bodyPct = body / (range + 1e-9m);
            var lowerTailPct = lowerTail / (range + 1e-9m);
            var upperTailPct = upperTail / (range + 1e-9m);

            // Hammer (long lower shadow, small body)
            if (lowerTailPct > 0.5m && bodyPct < 0.3m)
            {
                yield return new Recommendation(
                    symbol,
                    tf,
                    Name,
                    "BUY",
                    ComputeConfidence(bodyPct, lowerTailPct, b, isHammer: true),
                    b.Close,
                    "hammer"
                );
            }

            // Shooting star (long upper shadow, small body)
            if (upperTailPct > 0.5m && bodyPct < 0.3m)
            {
                yield return new Recommendation(
                    symbol,
                    tf,
                    Name,
                    "SELL",
                    ComputeConfidence(bodyPct, upperTailPct, b, isHammer: false),
                    b.Close,
                    "shooting-star"
                );
            }
        }

        private decimal ComputeConfidence(decimal bodyPct, decimal tailPct, TickerBar bar, bool isHammer)
        {
            // --- Base confidence from tail dominance ---
            var conf = 0.45m + (tailPct - 0.5m) * 0.4m; // stronger tail = higher confidence

            // --- Smaller body = stronger reversal signal ---
            conf *= (1.0m - bodyPct * 0.5m);

            // --- Volume factor (bar vs average range proxy) ---
            // If volume field exists and is >0, scale confidence a bit
            if (bar.Volume > 0)
            {
                // normalize against body size so hammers on high volume weigh more
                var volFactor = Math.Clamp((decimal)bar.Volume / (1m + bodyPct * 1000m), 0.5m, 2.0m);
                conf *= (0.85m + 0.15m * volFactor);
            }

            // Clamp to safe range
            return Math.Clamp(conf, 0.35m, 0.90m);
        }
    }
    public sealed class HammerReversalPlus : IPatternDetector
    {
        public string Name => "HammerReversal";
        private readonly decimal _minRangePct;
        private readonly decimal _tailToBody;

        public HammerReversalPlus(decimal minRangePct = 0.0005m, decimal tailToBody = 2m)
        {
            _minRangePct = minRangePct;
            _tailToBody = tailToBody;
        }

        public IEnumerable<Recommendation> Evaluate(string symbol, TimeSpan tf, IReadOnlyList<TickerBar> hist)
        {
            if (hist.Count < 4) yield break;

            var b = hist[^1];
            var range = b.High - b.Low;
            if (range <= 0) yield break;

            var refPx = b.Close == 0 ? 1m : b.Close;
            if (range / refPx < _minRangePct) yield break;

            var body = Math.Abs(b.Close - b.Open);
            if (body == 0) yield break;

            var lowerTail = Math.Min(b.Open, b.Close) - b.Low;
            var upperTail = b.High - Math.Max(b.Open, b.Close);

            decimal bodyTopFrac = (Math.Max(b.Open, b.Close) - b.Low) / range;
            decimal bodyBotFrac = (Math.Min(b.Open, b.Close) - b.Low) / range;

            // Simple prior-trend check
            bool priorDown = hist[^4].Close > hist[^3].Close && hist[^3].Close > hist[^2].Close;
            bool priorUp = hist[^4].Close < hist[^3].Close && hist[^3].Close < hist[^2].Close;

            bool hammerShape =
                lowerTail >= _tailToBody * body &&
                body / range <= 0.35m &&
                bodyTopFrac >= 0.70m;

            if (priorDown && hammerShape)
            {
                yield return new Recommendation(
                    symbol, tf, Name, "BUY",
                    ComputeConfidence(body, range, lowerTail, b, isHammer: true),
                    b.Close,
                    "hammer+trend"
                );
            }

            bool starShape =
                upperTail >= _tailToBody * body &&
                body / range <= 0.35m &&
                bodyBotFrac <= 0.30m;

            if (priorUp && starShape)
            {
                yield return new Recommendation(
                    symbol, tf, Name, "SELL",
                    ComputeConfidence(body, range, upperTail, b, isHammer: false),
                    b.Close,
                    "shooting-star+trend"
                );
            }
        }

        private decimal ComputeConfidence(decimal body, decimal range, decimal tail, TickerBar bar, bool isHammer)
        {
            // --- Tail strength relative to body and range ---
            var tailStrength = (tail / (body + 1e-9m)) + (tail / (range + 1e-9m));

            // --- Base confidence ---
            var conf = 0.55m + Math.Min(tailStrength, 4m) * 0.1m;

            // --- Body smallness bonus ---
            var bodyPct = body / (range + 1e-9m);
            conf *= (0.95m + (0.15m * (1 - bodyPct))); // smaller body = higher confidence

            // --- Volume factor ---
            if (bar.Volume > 0)
            {
                var volFactor = Math.Clamp((decimal)bar.Volume / (1m + bodyPct * 1000m), 0.5m, 2.0m);
                conf *= (0.85m + 0.15m * volFactor);
            }

            return Math.Clamp(conf, 0.45m, 0.92m);
        }
    }




    // Classic moving-average crossover on the latest CLOSED bar.
    // Emits:
    //   - BUY  when fast EMA crosses above slow EMA on the last close
    //   - SELL when fast EMA crosses below slow EMA on the last close
    public sealed class EmaCross : IPatternDetector
    {
        public string Name => "EmaCross";
        private readonly int _fast, _slow;

        public EmaCross(int fast, int slow) { _fast = fast; _slow = slow; }

        public IEnumerable<Recommendation> Evaluate(string sym, TimeSpan tf, IReadOnlyList<TickerBar> hist)
        {
            if (hist.Count < Math.Max(_fast, _slow) + 2) yield break;

            var closes = hist.Select(h => h.Close).ToList();

            decimal E(IReadOnlyList<decimal> xs, int p)
            {
                var k = 2m / (p + 1);
                decimal e = xs[0];
                for (int i = 1; i < xs.Count; i++)
                    e = xs[i] * k + e * (1 - k);
                return e;
            }

            var prev = closes.Take(closes.Count - 1).ToList();
            var fastPrev = E(prev, _fast);
            var slowPrev = E(prev, _slow);
            var fastNow = E(closes, _fast);
            var slowNow = E(closes, _slow);

            var last = hist[^1];

            if (fastPrev <= slowPrev && fastNow > slowNow)
            {
                yield return new Recommendation(
                    sym, tf, Name, "BUY",
                    ComputeConfidence(fastPrev, slowPrev, fastNow, slowNow, last, isBuy: true),
                    last.Close,
                    "fast>slow"
                );
            }

            if (fastPrev >= slowPrev && fastNow < slowNow)
            {
                yield return new Recommendation(
                    sym, tf, Name, "SELL",
                    ComputeConfidence(fastPrev, slowPrev, fastNow, slowNow, last, isBuy: false),
                    last.Close,
                    "fast<slow"
                );
            }
        }

        private decimal ComputeConfidence(
            decimal fastPrev, decimal slowPrev,
            decimal fastNow, decimal slowNow,
            TickerBar last,
            bool isBuy)
        {
            // --- Separation strength ---
            var sep = Math.Abs(fastNow - slowNow) / ((slowNow + fastNow) / 2m + 1e-9m);

            // --- Slope alignment (both rising or both falling) ---
            var fastSlope = fastNow - fastPrev;
            var slowSlope = slowNow - slowPrev;
            bool aligned = (fastSlope >= 0 && slowSlope >= 0) || (fastSlope <= 0 && slowSlope <= 0);

            // --- Base confidence ---
            var conf = 0.50m + Math.Min(sep * 10m, 0.12m); // larger separation = more conviction

            if (aligned)
                conf += 0.05m; // trend confirmation bonus

            // --- Volume factor ---
            if (last.Volume > 0)
            {
                var volFactor = Math.Clamp((decimal)last.Volume / (1m + Math.Abs((decimal)fastSlope)), 0.5m, 2.0m);
                conf *= (0.9m + 0.1m * volFactor);
            }

            return Math.Clamp(conf, 0.45m, 0.82m);
        }
    }
    // Fires only if the EMAs are actually pointing the same way.
    // Idea: detect the cross on the latest closed bar, but also require
    // the fast EMA's slope to be > 0 for BUY (and < 0 for SELL), and the
    // slow EMA not fighting the move.
    public sealed class EmaCrossSlope : IPatternDetector
    {
        public string Name => "EmaCrossSlope";
        private readonly int _fast, _slow, _slopeLookback;

        public EmaCrossSlope(int fast = 9, int slow = 21, int slopeLookback = 3)
        {
            _fast = fast;
            _slow = slow;
            _slopeLookback = slopeLookback;
        }

        public IEnumerable<Recommendation> Evaluate(string sym, TimeSpan tf, IReadOnlyList<TickerBar> hist)
        {
            int need = Math.Max(_fast, _slow) + Math.Max(2, _slopeLookback);
            if (hist.Count < need) yield break;

            var closes = hist.Select(h => h.Close).ToList();

            decimal EMA(IReadOnlyList<decimal> xs, int p)
            {
                var k = 2m / (p + 1);
                decimal e = xs[0];
                for (int i = 1; i < xs.Count; i++)
                    e = xs[i] * k + e * (1 - k);
                return e;
            }

            var prev = closes.Take(closes.Count - 1).ToList();
            var fPrev = EMA(prev, _fast);
            var sPrev = EMA(prev, _slow);
            var fNow = EMA(closes, _fast);
            var sNow = EMA(closes, _slow);

            var thenCloses = closes.Take(closes.Count - _slopeLookback).ToList();
            var fThen = EMA(thenCloses, _fast);
            var sThen = EMA(thenCloses, _slow);
            var fSlope = fNow - fThen;
            var sSlope = sNow - sThen;

            var last = hist[^1];

            if (fPrev <= sPrev && fNow > sNow && fSlope > 0m && sSlope >= 0m)
            {
                yield return new Recommendation(
                    sym, tf, Name, "BUY",
                    ComputeConfidence(fPrev, sPrev, fNow, sNow, fSlope, sSlope, last),
                    last.Close,
                    "cross & slopes↑"
                );
            }

            if (fPrev >= sPrev && fNow < sNow && fSlope < 0m && sSlope <= 0m)
            {
                yield return new Recommendation(
                    sym, tf, Name, "SELL",
                    ComputeConfidence(fPrev, sPrev, fNow, sNow, fSlope, sSlope, last),
                    last.Close,
                    "cross & slopes↓"
                );
            }
        }

        private decimal ComputeConfidence(
            decimal fPrev, decimal sPrev,
            decimal fNow, decimal sNow,
            decimal fSlope, decimal sSlope,
            TickerBar last)
        {
            // Separation: how far apart EMAs are post-cross
            var sep = Math.Abs(fNow - sNow) / ((fNow + sNow) / 2m + 1e-9m);

            // Slope strength: normalized by slow EMA to avoid raw price scale issues
            var slopeStrength = (Math.Abs(fSlope) + Math.Abs(sSlope)) / (Math.Abs(sNow) + 1e-9m);

            // Base confidence
            var conf = 0.53m
                       + Math.Min(sep * 10m, 0.12m)
                       + Math.Min(slopeStrength * 2m, 0.10m);

            // Volume factor
            if (last.Volume > 0)
            {
                var volFactor = Math.Clamp((decimal)last.Volume / (1m + Math.Abs((decimal)fSlope)), 0.5m, 2.0m);
                conf *= (0.9m + 0.1m * volFactor);
            }

            return Math.Clamp(conf, 0.48m, 0.85m);
        }
    }
    // Only signal if the *close* is also on the correct side of BOTH EMAs.
    // This avoids cases where EMAs crossed but price is still stuck in between.
    public sealed class EmaCrossPriceSide : IPatternDetector
    {
        public string Name => "EmaCrossPriceSide";
        private readonly int _fast, _slow;

        public EmaCrossPriceSide(int fast = 9, int slow = 21)
        { _fast = fast; _slow = slow; }

        public IEnumerable<Recommendation> Evaluate(string sym, TimeSpan tf, IReadOnlyList<TickerBar> hist)
        {
            if (hist.Count < Math.Max(_fast, _slow) + 2) yield break;

            var closes = hist.Select(h => h.Close).ToList();

            decimal EMA(IReadOnlyList<decimal> xs, int p)
            {
                var k = 2m / (p + 1);
                decimal e = xs[0];
                for (int i = 1; i < xs.Count; i++)
                    e = xs[i] * k + e * (1 - k);
                return e;
            }

            var prev = closes.Take(closes.Count - 1).ToList();
            var fPrev = EMA(prev, _fast);
            var sPrev = EMA(prev, _slow);
            var fNow = EMA(closes, _fast);
            var sNow = EMA(closes, _slow);

            var last = hist[^1];

            // BUY: cross up AND price > both EMAs
            if (fPrev <= sPrev && fNow > sNow && last.Close > fNow && last.Close > sNow)
            {
                yield return new Recommendation(
                    sym, tf, Name, "BUY",
                    ComputeConfidence(fPrev, sPrev, fNow, sNow, last, isBuy: true),
                    last.Close,
                    "cross & price above"
                );
            }

            // SELL: cross down AND price < both EMAs
            if (fPrev >= sPrev && fNow < sNow && last.Close < fNow && last.Close < sNow)
            {
                yield return new Recommendation(
                    sym, tf, Name, "SELL",
                    ComputeConfidence(fPrev, sPrev, fNow, sNow, last, isBuy: false),
                    last.Close,
                    "cross & price below"
                );
            }
        }

        private decimal ComputeConfidence(
            decimal fPrev, decimal sPrev,
            decimal fNow, decimal sNow,
            TickerBar last,
            bool isBuy)
        {
            // Separation strength between EMAs
            var sep = Math.Abs(fNow - sNow) / ((fNow + sNow) / 2m + 1e-9m);

            // Price distance beyond the slower EMA (confirmation strength)
            var dist = isBuy
                ? (last.Close - sNow) / (sNow + 1e-9m)
                : (sNow - last.Close) / (sNow + 1e-9m);

            // Base confidence
            var conf = 0.54m
                       + Math.Min(sep * 10m, 0.12m)
                       + Math.Min(dist * 5m, 0.10m);

            // Volume factor
            if (last.Volume > 0)
            {
                var volFactor = Math.Clamp((decimal)last.Volume / (1m + Math.Abs((decimal)(fNow - sNow))), 0.5m, 2.0m);
                conf *= (0.9m + 0.1m * volFactor);
            }

            return Math.Clamp(conf, 0.50m, 0.86m);
        }
    }
    // Wait for the cross bar (c) AND a confirmation bar (d) that still has fast on the correct side.
    // Reduces one-bar fake crosses at the cost of latency.
    public sealed class EmaCrossConfirm : IPatternDetector
    {
        public string Name => "EmaCrossConfirm";
        private readonly int _fast, _slow;

        public EmaCrossConfirm(int fast = 9, int slow = 21)
        { _fast = fast; _slow = slow; }

        public IEnumerable<Recommendation> Evaluate(string sym, TimeSpan tf, IReadOnlyList<TickerBar> hist)
        {
            if (hist.Count < Math.Max(_fast, _slow) + 3) yield break;

            var closes = hist.Select(h => h.Close).ToList();

            decimal EMA(IReadOnlyList<decimal> xs, int p)
            {
                var k = 2m / (p + 1);
                decimal e = xs[0];
                for (int i = 1; i < xs.Count; i++)
                    e = xs[i] * k + e * (1 - k);
                return e;
            }

            // checkpoints
            var upToB = closes.Take(closes.Count - 2).ToList();
            var upToC = closes.Take(closes.Count - 1).ToList();
            var upToD = closes;

            var fB = EMA(upToB, _fast); var sB = EMA(upToB, _slow);
            var fC = EMA(upToC, _fast); var sC = EMA(upToC, _slow);
            var fD = EMA(upToD, _fast); var sD = EMA(upToD, _slow);

            var last = hist[^1];

            if (fB <= sB && fC > sC && fD > sD)
            {
                yield return new Recommendation(
                    sym, tf, Name, "BUY",
                    ComputeConfidence(fD, sD, last, isBuy: true),
                    last.Close,
                    "cross+confirm↑"
                );
            }

            if (fB >= sB && fC < sC && fD < sD)
            {
                yield return new Recommendation(
                    sym, tf, Name, "SELL",
                    ComputeConfidence(fD, sD, last, isBuy: false),
                    last.Close,
                    "cross+confirm↓"
                );
            }
        }

        private decimal ComputeConfidence(decimal fNow, decimal sNow, TickerBar last, bool isBuy)
        {
            // EMA separation after confirmation
            var sep = Math.Abs(fNow - sNow) / ((fNow + sNow) / 2m + 1e-9m);

            // Price distance beyond slow EMA
            var dist = isBuy
                ? (last.Close - sNow) / (sNow + 1e-9m)
                : (sNow - last.Close) / (sNow + 1e-9m);

            var conf = 0.56m
                       + Math.Min(sep * 8m, 0.10m)   // EMA separation contributes
                       + Math.Min(dist * 4m, 0.08m); // price distance contributes

            // Volume factor (optional bump)
            if (last.Volume > 0)
            {
                var volFactor = Math.Clamp((decimal)last.Volume / (1m + Math.Abs(fNow - sNow)), 0.5m, 2.0m);
                conf *= (0.9m + 0.1m * volFactor);
            }

            return Math.Clamp(conf, 0.52m, 0.88m);
        }
    }
    // Require the absolute distance between fast and slow EMAs AFTER the cross
    // to exceed a small percentage of price. Filters out near-equal, choppy crosses.
    public sealed class EmaCrossWide : IPatternDetector
    {
        public string Name => "EmaCrossWide";
        private readonly int _fast, _slow;
        private readonly decimal _minSepPct;

        public EmaCrossWide(int fast = 9, int slow = 21, decimal minSeparationPct = 0.0005m)
        {
            _fast = fast;
            _slow = slow;
            _minSepPct = minSeparationPct;
        }

        public IEnumerable<Recommendation> Evaluate(string sym, TimeSpan tf, IReadOnlyList<TickerBar> hist)
        {
            if (hist.Count < Math.Max(_fast, _slow) + 2) yield break;

            var closes = hist.Select(h => h.Close).ToList();

            decimal EMA(IReadOnlyList<decimal> xs, int p)
            {
                var k = 2m / (p + 1);
                decimal e = xs[0];
                for (int i = 1; i < xs.Count; i++)
                    e = xs[i] * k + e * (1 - k);
                return e;
            }

            var prev = closes.Take(closes.Count - 1).ToList();
            var fPrev = EMA(prev, _fast);
            var sPrev = EMA(prev, _slow);
            var fNow = EMA(closes, _fast);
            var sNow = EMA(closes, _slow);

            var last = hist[^1];
            var refPx = last.Close == 0 ? 1m : last.Close;

            // Separation (core filter for this detector)
            var sep = Math.Abs(fNow - sNow) / refPx;

            if (fPrev <= sPrev && fNow > sNow && sep >= _minSepPct)
            {
                yield return new Recommendation(
                    sym, tf, Name, "BUY",
                    ComputeConfidence(fNow, sNow, last, sep, isBuy: true),
                    last.Close,
                    $"cross wide ({sep:P2})"
                );
            }

            if (fPrev >= sPrev && fNow < sNow && sep >= _minSepPct)
            {
                yield return new Recommendation(
                    sym, tf, Name, "SELL",
                    ComputeConfidence(fNow, sNow, last, sep, isBuy: false),
                    last.Close,
                    $"cross wide ({sep:P2})"
                );
            }
        }

        private decimal ComputeConfidence(decimal fNow, decimal sNow, TickerBar last, decimal sep, bool isBuy)
        {
            // Normalized separation is the star of the show
            var sepBoost = Math.Min(sep * 12m, 0.15m);

            // Price clearance relative to the slow EMA
            var dist = isBuy
                ? (last.Close - sNow) / (sNow + 1e-9m)
                : (sNow - last.Close) / (sNow + 1e-9m);
            var distBoost = Math.Min(dist * 5m, 0.10m);

            // Confidence = base + sep + distance
            var conf = 0.55m + sepBoost + distBoost;

            // Optional: reward volume
            if (last.Volume > 0)
            {
                var volFactor = Math.Clamp((decimal)last.Volume / (1m + Math.Abs(fNow - sNow)), 0.5m, 2.0m);
                conf *= (0.9m + 0.1m * volFactor);
            }

            return Math.Clamp(conf, 0.53m, 0.90m);
        }
    }



    // Emits a BUY when RSI is <= _low, and a SELL when RSI is >= _high.
    // RSI here is computed using *simple* average gains/losses over the last _len deltas
    // (not Wilder's smoothed RSI). That keeps it lightweight but a bit noisier.
    public sealed class RsiExtreme : IPatternDetector
    {
        public string Name => "RsiExtreme";

        private readonly int _len;             // RSI lookback (e.g., 14)
        private readonly decimal _low, _high;  // thresholds (e.g., 30 / 70)

        public RsiExtreme(int len = 14, decimal low = 30m, decimal high = 70m)
        {
            _len = len;
            _low = low;
            _high = high;
        }

        public IEnumerable<Recommendation> Evaluate(string sym, TimeSpan tf, IReadOnlyList<TickerBar> hist)
        {
            if (hist.Count < _len + 1)
                yield break;

            var closes = hist.Select(h => h.Close).ToArray();

            decimal RSI(int n)
            {
                decimal g = 0m, l = 0m;
                for (int i = closes.Length - n; i < closes.Length; i++)
                {
                    var d = closes[i] - closes[i - 1];
                    if (d > 0) g += d; else l -= d;
                }
                if (g + l == 0) return 50m;

                var rs = l == 0 ? 999m : g / (l == 0 ? 1m : l);
                var r = 100m - 100m / (1m + rs);
                return Math.Clamp(r, 0m, 100m);
            }

            var rsi = RSI(_len);
            var last = hist[^1];

            if (rsi <= _low)
            {
                yield return new Recommendation(
                    sym, tf, Name, "BUY",
                    ComputeConfidence(rsi, isBuy: true),
                    last.Close,
                    $"rsi={rsi:F1}"
                );
            }

            if (rsi >= _high)
            {
                yield return new Recommendation(
                    sym, tf, Name, "SELL",
                    ComputeConfidence(rsi, isBuy: false),
                    last.Close,
                    $"rsi={rsi:F1}"
                );
            }
        }

        private decimal ComputeConfidence(decimal rsi, bool isBuy)
        {
            // Distance from the threshold, normalized into [0, 0.2]
            decimal dist;
            if (isBuy)
                dist = (_low - rsi) / 20m; // deeper oversold
            else
                dist = (rsi - _high) / 20m; // deeper overbought

            var boost = Math.Min(dist, 0.20m);

            // Base 0.50 + boost → up to ~0.70 for very stretched extremes
            var conf = 0.50m + boost;

            return Math.Clamp(conf, 0.50m, 0.70m);
        }
    }
    // Same thresholds as RsiExtreme, but RSI is the canonical Wilder version.
    // We recompute over the whole series to get the smoothed last value.
    public sealed class RsiExtremeWilder : IPatternDetector
    {
        public string Name => "RsiExtreme";

        private readonly int _len;
        private readonly decimal _low, _high;

        public RsiExtremeWilder(int len = 14, decimal low = 30m, decimal high = 70m)
        {
            _len = len;
            _low = low;
            _high = high;
        }

        public IEnumerable<Recommendation> Evaluate(string sym, TimeSpan tf, IReadOnlyList<TickerBar> hist)
        {
            if (hist.Count < _len + 1) yield break;

            var xs = hist.Select(h => h.Close).ToList();

            decimal WilderRsi(IReadOnlyList<decimal> s, int n)
            {
                if (s.Count < n + 1) return 50m;

                decimal gain = 0m, loss = 0m;
                for (int i = 1; i <= n; i++)
                {
                    var d = s[i] - s[i - 1];
                    if (d > 0) gain += d; else loss -= d;
                }
                decimal avgGain = gain / n, avgLoss = loss / n;

                for (int i = n + 1; i < s.Count; i++)
                {
                    var d = s[i] - s[i - 1];
                    var g = d > 0 ? d : 0m;
                    var l = d < 0 ? -d : 0m;
                    avgGain = (avgGain * (n - 1) + g) / n;
                    avgLoss = (avgLoss * (n - 1) + l) / n;
                }

                if (avgLoss == 0) return 100m;
                var rs = avgGain / (avgLoss == 0 ? 1m : avgLoss);
                var rsi = 100m - 100m / (1m + rs);
                return Math.Clamp(rsi, 0m, 100m);
            }

            var rsi = WilderRsi(xs, _len);
            var last = hist[^1];

            if (rsi <= _low)
            {
                yield return new Recommendation(
                    sym, tf, Name, "BUY",
                    ComputeConfidence(rsi, isBuy: true),
                    last.Close,
                    $"rsi_w={rsi:F1}"
                );
            }

            if (rsi >= _high)
            {
                yield return new Recommendation(
                    sym, tf, Name, "SELL",
                    ComputeConfidence(rsi, isBuy: false),
                    last.Close,
                    $"rsi_w={rsi:F1}"
                );
            }
        }

        private decimal ComputeConfidence(decimal rsi, bool isBuy)
        {
            decimal dist;
            if (isBuy)
                dist = (_low - rsi) / 20m;   // deeper oversold = stronger
            else
                dist = (rsi - _high) / 20m;  // deeper overbought = stronger

            var boost = Math.Min(dist, 0.20m);

            // Base 0.51 → up to ~0.71 at extreme readings
            var conf = 0.51m + boost;

            return Math.Clamp(conf, 0.51m, 0.71m);
        }
    }
    // Enter when RSI *crosses back* through the threshold (classic reversion timing).
    // BUY: prev <= low  && now > low
    // SELL: prev >= high && now < high
    public sealed class RsiCrossThreshold : IPatternDetector
    {
        public string Name => "RsiCross";
        private readonly int _len;
        private readonly decimal _low, _high;

        public RsiCrossThreshold(int len = 14, decimal low = 30m, decimal high = 70m)
        {
            _len = len;
            _low = low;
            _high = high;
        }

        public IEnumerable<Recommendation> Evaluate(string sym, TimeSpan tf, IReadOnlyList<TickerBar> hist)
        {
            if (hist.Count < _len + 2) yield break; // need prev and now

            var closes = hist.Select(h => h.Close).ToList();

            decimal SimpleRsiAt(IReadOnlyList<decimal> s, int upto)
            {
                if (upto < _len) return 50m;
                decimal g = 0m, l = 0m;
                for (int i = upto - _len + 1; i <= upto; i++)
                {
                    var d = s[i] - s[i - 1];
                    if (d > 0) g += d; else l -= d;
                }
                if (g + l == 0) return 50m;
                var rs = l == 0 ? 999m : g / (l == 0 ? 1m : l);
                var r = 100m - 100m / (1m + rs);
                return Math.Clamp(r, 0m, 100m);
            }

            var rsiPrev = SimpleRsiAt(closes, closes.Count - 2);
            var rsiNow = SimpleRsiAt(closes, closes.Count - 1);
            var last = hist[^1];

            if (rsiPrev <= _low && rsiNow > _low)
            {
                yield return new Recommendation(
                    sym, tf, Name, "BUY",
                    ComputeConfidence(rsiNow, _low, isBuy: true),
                    last.Close,
                    $"rsi↑ {_low:F0}"
                );
            }

            if (rsiPrev >= _high && rsiNow < _high)
            {
                yield return new Recommendation(
                    sym, tf, Name, "SELL",
                    ComputeConfidence(rsiNow, _high, isBuy: false),
                    last.Close,
                    $"rsi↓ {_high:F0}"
                );
            }
        }

        private decimal ComputeConfidence(decimal rsiNow, decimal threshold, bool isBuy)
        {
            decimal dist;
            if (isBuy)
                dist = (rsiNow - threshold) / 20m;   // stronger if RSI pushes further above low
            else
                dist = (threshold - rsiNow) / 20m;   // stronger if RSI pushes further below high

            var boost = Math.Min(dist, 0.20m);

            // Base 0.52 → up to ~0.72 at decisive crosses
            var conf = 0.52m + boost;

            return Math.Clamp(conf, 0.52m, 0.72m);
        }
    }
    // Use the 50 line as a trend gate.
    // BUY when RSI crosses above 50; SELL when it crosses below 50.
    // Cleaner if you want trend participation rather than pure mean-reversion.
    public sealed class RsiRegime50 : IPatternDetector
    {
        public string Name => "Rsi50";
        private readonly int _len;
        private readonly decimal _mid;
        private readonly decimal _minDelta;

        public RsiRegime50(int len = 14, decimal mid = 50m, decimal minDelta = 0.5m)
        {
            _len = len;
            _mid = mid;
            _minDelta = minDelta;
        }

        public IEnumerable<Recommendation> Evaluate(string sym, TimeSpan tf, IReadOnlyList<TickerBar> hist)
        {
            if (hist.Count < _len + 2) yield break;

            var closes = hist.Select(h => h.Close).ToArray();

            decimal RsiSimple(int n)
            {
                decimal g = 0m, l = 0m;
                for (int i = closes.Length - n; i < closes.Length; i++)
                {
                    var d = closes[i] - closes[i - 1];
                    if (d > 0) g += d; else l -= d;
                }
                if (g + l == 0) return 50m;
                var rs = l == 0 ? 999m : g / (l == 0 ? 1m : l);
                return Math.Clamp(100m - 100m / (1m + rs), 0m, 100m);
            }

            decimal RsiPrev()
            {
                var prevCloses = closes.Take(closes.Length - 1).ToArray();
                decimal g = 0m, l = 0m;
                for (int i = prevCloses.Length - _len; i < prevCloses.Length; i++)
                {
                    var d = prevCloses[i] - prevCloses[i - 1];
                    if (d > 0) g += d; else l -= d;
                }
                if (g + l == 0) return 50m;
                var rs = l == 0 ? 999m : g / (l == 0 ? 1m : l);
                return Math.Clamp(100m - 100m / (1m + rs), 0m, 100m);
            }

            var rPrev = RsiPrev();
            var rNow = RsiSimple(_len);
            var last = hist[^1];

            if (rPrev <= _mid - _minDelta && rNow >= _mid + _minDelta)
            {
                yield return new Recommendation(
                    sym, tf, Name, "BUY",
                    ComputeConfidence(rNow, _mid, isBuy: true),
                    last.Close,
                    "rsi>50"
                );
            }

            if (rPrev >= _mid + _minDelta && rNow <= _mid - _minDelta)
            {
                yield return new Recommendation(
                    sym, tf, Name, "SELL",
                    ComputeConfidence(rNow, _mid, isBuy: false),
                    last.Close,
                    "rsi<50"
                );
            }
        }

        private decimal ComputeConfidence(decimal rsiNow, decimal mid, bool isBuy)
        {
            decimal dist;
            if (isBuy)
                dist = (rsiNow - mid) / 25m;   // stronger if RSI pulls further above 50
            else
                dist = (mid - rsiNow) / 25m;   // stronger if RSI pulls further below 50

            var boost = Math.Min(dist, 0.25m);

            // Base 0.53 → up to ~0.78 at decisive breaks
            var conf = 0.53m + boost;

            return Math.Clamp(conf, 0.53m, 0.78m);
        }
    }
    // After an oversold/overbought event, require RSI to pivot and continue.
    // BUY: rsi[-3] <= low, rsi[-2] > rsi[-3], rsi[-1] > rsi[-2]  (rising out of OS)
    // SELL: symmetric for OB.
    public sealed class RsiFailureSwingLite : IPatternDetector
    {
        public string Name => "RsiFailureSwing";
        private readonly int _len;
        private readonly decimal _low, _high;

        public RsiFailureSwingLite(int len = 14, decimal low = 30m, decimal high = 70m)
        {
            _len = len;
            _low = low;
            _high = high;
        }

        public IEnumerable<Recommendation> Evaluate(string sym, TimeSpan tf, IReadOnlyList<TickerBar> hist)
        {
            if (hist.Count < _len + 3) yield break;

            var closes = hist.Select(h => h.Close).ToList();

            decimal RsiAt(int upto)
            {
                if (upto < _len) return 50m;
                decimal g = 0m, l = 0m;
                for (int i = upto - _len + 1; i <= upto; i++)
                {
                    var d = closes[i] - closes[i - 1];
                    if (d > 0) g += d; else l -= d;
                }
                if (g + l == 0) return 50m;
                var rs = l == 0 ? 999m : g / (l == 0 ? 1m : l);
                return Math.Clamp(100m - 100m / (1m + rs), 0m, 100m);
            }

            int i0 = closes.Count - 1, i1 = i0 - 1, i2 = i0 - 2;
            var r0 = RsiAt(i0);
            var r1 = RsiAt(i1);
            var r2 = RsiAt(i2);
            var last = hist[^1];

            if (r2 <= _low && r1 > r2 && r0 > r1)
            {
                yield return new Recommendation(
                    sym, tf, Name, "BUY",
                    ComputeConfidence(r2, r0, isBuy: true),
                    last.Close,
                    "fail-swing↑"
                );
            }

            if (r2 >= _high && r1 < r2 && r0 < r1)
            {
                yield return new Recommendation(
                    sym, tf, Name, "SELL",
                    ComputeConfidence(r2, r0, isBuy: false),
                    last.Close,
                    "fail-swing↓"
                );
            }
        }

        private decimal ComputeConfidence(decimal rsiStart, decimal rsiNow, bool isBuy)
        {
            decimal dist;
            if (isBuy)
                dist = (rsiNow - rsiStart) / 20m; // how strong RSI bounced upward
            else
                dist = (rsiStart - rsiNow) / 20m; // how strong RSI rejected downward

            var boost = Math.Min(dist, 0.25m);

            // Base 0.53 → up to ~0.78 on strong swings
            var conf = 0.53m + boost;

            return Math.Clamp(conf, 0.53m, 0.78m);
        }
    }
    // Bullish divergence: price makes a lower low while RSI makes a higher low.
    // Bearish divergence: price makes a higher high while RSI makes a lower high.
    // Uses basic 3-bar pivots and a lookback window to find the last two pivots.
    public sealed class RsiDivergence : IPatternDetector
    {
        public string Name => "RsiDivergence";
        private readonly int _len;       // RSI length
        private readonly int _lookback;  // pivot search window (e.g., 20)

        public RsiDivergence(int len = 14, int lookback = 20)
        {
            _len = len;
            _lookback = lookback;
        }

        public IEnumerable<Recommendation> Evaluate(string sym, TimeSpan tf, IReadOnlyList<TickerBar> hist)
        {
            if (hist.Count < Math.Max(_len + 2, _lookback + 2)) yield break;

            var closes = hist.Select(h => h.Close).ToList();

            // Wilder RSI (smoother than raw RSI)
            decimal WilderRsi(IReadOnlyList<decimal> s, int n)
            {
                if (s.Count < n + 1) return 50m;
                decimal gain = 0m, loss = 0m;
                for (int i = 1; i <= n; i++)
                {
                    var d = s[i] - s[i - 1];
                    if (d > 0) gain += d; else loss -= d;
                }
                decimal avgG = gain / n, avgL = loss / n;
                for (int i = n + 1; i < s.Count; i++)
                {
                    var d = s[i] - s[i - 1];
                    var g = d > 0 ? d : 0m;
                    var l = d < 0 ? -d : 0m;
                    avgG = (avgG * (n - 1) + g) / n;
                    avgL = (avgL * (n - 1) + l) / n;
                }
                if (avgL == 0) return 100m;
                var rs = avgG / (avgL == 0 ? 1m : avgL);
                return Math.Clamp(100m - 100m / (1m + rs), 0m, 100m);
            }

            // Build RSI series
            var rsiSeries = new List<decimal>(closes.Count);
            for (int i = 0; i < closes.Count; i++)
                rsiSeries.Add(WilderRsi(closes.Take(i + 1).ToList(), _len));

            // Simple 3-bar pivot scan
            bool IsLow(int i) => i > 0 && i < closes.Count - 1 && closes[i] < closes[i - 1] && closes[i] < closes[i + 1];
            bool IsHigh(int i) => i > 0 && i < closes.Count - 1 && closes[i] > closes[i - 1] && closes[i] > closes[i + 1];

            int start = Math.Max(1, closes.Count - _lookback);
            var priceLows = new List<int>();
            var priceHighs = new List<int>();
            for (int i = start; i < closes.Count - 1; i++)
            {
                if (IsLow(i)) priceLows.Add(i);
                if (IsHigh(i)) priceHighs.Add(i);
            }

            var last = hist[^1];

            // Bullish divergence: lower price low, higher RSI low
            if (priceLows.Count >= 2)
            {
                int i1 = priceLows[^2], i2 = priceLows[^1];
                var p1 = closes[i1]; var p2 = closes[i2];
                var r1 = rsiSeries[i1]; var r2 = rsiSeries[i2];

                if (p2 < p1 && r2 > r1)
                    yield return new(sym, tf, Name, "BUY",
                        ComputeConfidence(p1, p2, r1, r2, true),
                        last.Close,
                        "bull-div");
            }

            // Bearish divergence: higher price high, lower RSI high
            if (priceHighs.Count >= 2)
            {
                int i1 = priceHighs[^2], i2 = priceHighs[^1];
                var p1 = closes[i1]; var p2 = closes[i2];
                var r1 = rsiSeries[i1]; var r2 = rsiSeries[i2];

                if (p2 > p1 && r2 < r1)
                    yield return new(sym, tf, Name, "SELL",
                        ComputeConfidence(p1, p2, r1, r2, false),
                        last.Close,
                        "bear-div");
            }
        }

        private decimal ComputeConfidence(decimal p1, decimal p2, decimal r1, decimal r2, bool isBull)
        {
            // Price moved "further" while RSI reversed — stronger divergence = higher confidence
            var priceMove = Math.Abs((p2 - p1) / (p1 == 0 ? 1 : p1));
            var rsiMove = Math.Abs(r2 - r1) / 100m;

            // Composite: price move adds context, RSI reversal is key
            var strength = 0.6m * rsiMove + 0.4m * priceMove;

            // Base ~0.56 → up to ~0.80 if divergence is very strong
            var conf = 0.56m + Math.Min(strength, 0.24m);
            return Math.Clamp(conf, 0.56m, 0.80m);
        }
    }
    // Replace static 30/70 with rolling percentiles of RSI (e.g., 20th/80th).
    // Useful when volatility/regime shifts make fixed bands too tight/loose.
    public sealed class RsiAdaptiveBands : IPatternDetector
    {
        public string Name => "RsiAdaptive";
        private readonly int _len;           // RSI length
        private readonly int _bandLookback;  // window for percentile calc
        private readonly decimal _lowPct;    // e.g., 0.20m = 20th percentile
        private readonly decimal _highPct;   // e.g., 0.80m = 80th percentile

        public RsiAdaptiveBands(int len = 14, int bandLookback = 100, decimal lowPct = 0.20m, decimal highPct = 0.80m)
        {
            _len = len;
            _bandLookback = bandLookback;
            _lowPct = lowPct;
            _highPct = highPct;
        }

        public IEnumerable<Recommendation> Evaluate(string sym, TimeSpan tf, IReadOnlyList<TickerBar> hist)
        {
            int need = Math.Max(_len + 1, _bandLookback + _len);
            if (hist.Count < need) yield break;

            var closes = hist.Select(h => h.Close).ToList();

            decimal SimpleRsiAt(IReadOnlyList<decimal> s, int upto)
            {
                if (upto < _len) return 50m;
                decimal g = 0m, l = 0m;
                for (int i = upto - _len + 1; i <= upto; i++)
                {
                    var d = s[i] - s[i - 1];
                    if (d > 0) g += d; else l -= d;
                }
                if (g + l == 0) return 50m;
                var rs = l == 0 ? 999m : g / (l == 0 ? 1m : l);
                return Math.Clamp(100m - 100m / (1m + rs), 0m, 100m);
            }

            // Build last 'bandLookback' RSI values
            var rsiVals = new List<decimal>(_bandLookback);
            int start = closes.Count - _bandLookback;
            for (int i = start; i < closes.Count; i++)
                rsiVals.Add(SimpleRsiAt(closes, i));

            // Compute percentile bands
            rsiVals.Sort();
            int loIdx = (int)Math.Clamp((double)Math.Round(_lowPct * (rsiVals.Count - 1)), 0, rsiVals.Count - 1);
            int hiIdx = (int)Math.Clamp((double)Math.Round(_highPct * (rsiVals.Count - 1)), 0, rsiVals.Count - 1);
            var lowBand = rsiVals[loIdx];
            var highBand = rsiVals[hiIdx];

            var last = hist[^1];
            var rNow = SimpleRsiAt(closes, closes.Count - 1);

            // If RSI is at/beyond bands, compute confidence dynamically
            if (rNow <= lowBand)
            {
                yield return new(sym, tf, Name, "BUY",
                    ComputeConfidence(rNow, lowBand, true),
                    last.Close,
                    $"rsi≤p{_lowPct:P0} ({lowBand:F1})");
            }

            if (rNow >= highBand)
            {
                yield return new(sym, tf, Name, "SELL",
                    ComputeConfidence(rNow, highBand, false),
                    last.Close,
                    $"rsi≥p{_highPct:P0} ({highBand:F1})");
            }
        }

        private decimal ComputeConfidence(decimal rNow, decimal band, bool isLow)
        {
            // Distance from band, normalized by RSI scale (0..100)
            var distance = isLow
                ? (band - rNow) / 100m   // how far below the low band
                : (rNow - band) / 100m;  // how far above the high band

            // Base ~0.52, scale up with distance, cap ~0.75
            var conf = 0.52m + Math.Min(distance * 2m, 0.23m);
            return Math.Clamp(conf, 0.52m, 0.75m);
        }
    }







    // Detects a volatility "squeeze" via narrow Bollinger Bands, then trades a breakout.
    // Implementation notes:
    // - Squeeze condition: current BB width (2σ) / mid ≦ _widthPct
    // - Breakout condition: last bar CLOSE breaks the previous bar's High/Low by _breakPct
    // - Uses population variance over the last _period closes (simple mean/variance).
    public sealed class BollSqueezeBreakout : IPatternDetector
    {
        public string Name => "BollSqueezeBreakout";

        private readonly int _period;
        private readonly decimal _widthPct, _breakPct;

        public BollSqueezeBreakout(int period = 20, decimal widthPct = 0.012m, decimal breakPct = 0.003m)
        {
            _period = period;
            _widthPct = widthPct;
            _breakPct = breakPct;
        }

        public IEnumerable<Recommendation> Evaluate(string sym, TimeSpan tf, IReadOnlyList<TickerBar> hist)
        {
            if (hist.Count < _period + 2) yield break;

            var closes = hist.Select(h => h.Close).ToArray();
            var slice = closes.Length > _period
                ? closes[(closes.Length - _period)..]
                : closes;

            var mid = slice.Average();
            var varv = slice.Select(v => (v - mid) * (v - mid)).Average();
            var std = (decimal)Math.Sqrt((double)varv);

            var upper = mid + 2m * std;
            var lower = mid - 2m * std;
            var width = (upper - lower) / (mid == 0 ? 1 : mid);

            var last = hist[^1];
            var prev = hist[^2];

            if (width < _widthPct)
            {
                // Upside breakout
                if (last.Close >= prev.High * (1 + _breakPct))
                {
                    yield return new Recommendation(
                        sym, tf, Name, "BUY",
                        ComputeConfidence(width, last.Close, prev.High, true),
                        last.Close,
                        $"width={width:P2}"
                    );
                }

                // Downside breakout
                if (last.Close <= prev.Low * (1 - _breakPct))
                {
                    yield return new Recommendation(
                        sym, tf, Name, "SELL",
                        ComputeConfidence(width, last.Close, prev.Low, false),
                        last.Close,
                        $"width={width:P2}"
                    );
                }
            }
        }

        private decimal ComputeConfidence(decimal width, decimal close, decimal refPx, bool isUp)
        {
            // 1. Narrower squeeze → higher base confidence
            var squeezeStrength = 1m - (width / _widthPct);
            squeezeStrength = Math.Clamp(squeezeStrength, 0m, 1m);

            // 2. Breakout distance from refPx (scaled by refPx)
            var breakoutDist = isUp
                ? (close - refPx) / refPx
                : (refPx - close) / refPx;
            breakoutDist = Math.Clamp(breakoutDist / _breakPct, 0m, 2m); // 0 = at threshold, 2 = 2× buffer

            // Combine: base 0.54, add up to +0.20 from squeeze + breakout
            var conf = 0.54m + (squeezeStrength * 0.10m) + (breakoutDist * 0.10m);
            return Math.Clamp(conf, 0.54m, 0.74m);
        }
    }
    public sealed class BollSqueezeBandBreak : IPatternDetector
    {
        public string Name => "BollSqueezeBandBreak";

        private readonly int _period;
        private readonly decimal _widthPct;
        private readonly decimal _bandBreakPct;

        public BollSqueezeBandBreak(int period = 20, decimal widthPct = 0.012m, decimal bandBreakPct = 0.000m)
        {
            _period = period;
            _widthPct = widthPct;
            _bandBreakPct = bandBreakPct;
        }

        public IEnumerable<Recommendation> Evaluate(string sym, TimeSpan tf, IReadOnlyList<TickerBar> hist)
        {
            if (hist.Count < _period + 1) yield break;

            var closes = hist.Select(h => h.Close).ToArray();
            var slice = closes[(closes.Length - _period)..];
            var mid = slice.Average();
            var varv = slice.Select(v => (v - mid) * (v - mid)).Average();
            var std = (decimal)Math.Sqrt((double)varv);

            var upper = mid + 2m * std;
            var lower = mid - 2m * std;
            var width = (upper - lower) / (mid == 0 ? 1 : mid);

            var last = hist[^1];

            if (width < _widthPct)
            {
                if (last.Close >= upper * (1 + _bandBreakPct))
                {
                    yield return new Recommendation(
                        sym, tf, Name, "BUY",
                        ComputeConfidence(width, last.Close, upper, true),
                        last.Close,
                        $"band↑ width={width:P2}"
                    );
                }

                if (last.Close <= lower * (1 - _bandBreakPct))
                {
                    yield return new Recommendation(
                        sym, tf, Name, "SELL",
                        ComputeConfidence(width, last.Close, lower, false),
                        last.Close,
                        $"band↓ width={width:P2}"
                    );
                }
            }
        }

        private decimal ComputeConfidence(decimal width, decimal close, decimal band, bool isUp)
        {
            // 1. Tighter squeeze → higher base confidence
            var squeezeStrength = 1m - (width / _widthPct);
            squeezeStrength = Math.Clamp(squeezeStrength, 0m, 1m);

            // 2. Breakout distance beyond the band
            var breakoutDist = isUp
                ? (close - band) / band
                : (band - close) / band;

            breakoutDist = Math.Clamp(breakoutDist / (_bandBreakPct == 0 ? 0.001m : _bandBreakPct), 0m, 2m);

            // Combine: baseline 0.54, add squeeze + breakout factors
            var conf = 0.54m + (squeezeStrength * 0.10m) + (breakoutDist * 0.10m);
            return Math.Clamp(conf, 0.54m, 0.74m);
        }
    }
    public sealed class BollSqueezeConfirm : IPatternDetector
    {
        public string Name => "BollSqueezeConfirm";
        private readonly int _period;
        private readonly decimal _widthPct;
        private readonly decimal _breakPct;

        public BollSqueezeConfirm(int period = 20, decimal widthPct = 0.012m, decimal breakPct = 0.003m)
        {
            _period = period;
            _widthPct = widthPct;
            _breakPct = breakPct;
        }

        public IEnumerable<Recommendation> Evaluate(string sym, TimeSpan tf, IReadOnlyList<TickerBar> hist)
        {
            if (hist.Count < _period + 3) yield break;

            var closes = hist.Select(h => h.Close).ToArray();
            var slice = closes[(closes.Length - _period - 1)..^1]; // window ending at cross bar
            var mid = slice.Average();
            var varv = slice.Select(v => (v - mid) * (v - mid)).Average();
            var std = (decimal)Math.Sqrt((double)varv);
            var width = (2m * std) / (mid == 0 ? 1 : mid);

            var b = hist[^3]; // pre-break bar
            var c = hist[^2]; // cross/break bar
            var d = hist[^1]; // confirmation bar

            if (width >= _widthPct) yield break; // not in squeeze → skip

            // --- Confidence model ---
            // Base from squeeze: narrower width → stronger
            decimal squeezeScore = 1m - (width / _widthPct); // 0..1
            squeezeScore = Math.Clamp(squeezeScore, 0m, 1m);

            // Confirmation strength: % follow-through relative to break close
            decimal confStrength = Math.Abs(d.Close - c.Close) / (c.Close == 0 ? 1 : c.Close);
            confStrength = Math.Clamp(confStrength * 5m, 0m, 1m); // scale then cap

            // Blend them
            decimal confidence = 0.45m + 0.15m * squeezeScore + 0.15m * confStrength;
            confidence = Math.Clamp(confidence, 0.45m, 0.75m);

            // --- Signals ---
            if (c.Close >= b.High * (1 + _breakPct) && d.Close > c.Close)
                yield return new(sym, tf, Name, "BUY", confidence, d.Close,
                    $"squeeze→up(confirm) w={width:P2}");

            if (c.Close <= b.Low * (1 - _breakPct) && d.Close < c.Close)
                yield return new(sym, tf, Name, "SELL", confidence, d.Close,
                    $"squeeze→down(confirm) w={width:P2}");
        }
    }
    public sealed class BollInsideKeltnerBreakout : IPatternDetector
    {
        public string Name => "BollInsideKeltner";
        private readonly int _period;
        private readonly decimal _kcMult;
        private readonly decimal _bbMult;
        private readonly decimal _breakPct;

        public BollInsideKeltnerBreakout(
            int period = 20, decimal kcMult = 1.5m, decimal bbMult = 2m, decimal breakPct = 0.003m)
        {
            _period = period;
            _kcMult = kcMult;
            _bbMult = bbMult;
            _breakPct = breakPct;
        }

        public IEnumerable<Recommendation> Evaluate(string sym, TimeSpan tf, IReadOnlyList<TickerBar> hist)
        {
            if (hist.Count < _period + 2) yield break;

            var closes = hist.Select(h => h.Close).ToList();

            // EMA
            decimal EMA(IReadOnlyList<decimal> xs, int p)
            {
                var k = 2m / (p + 1);
                decimal e = xs[0];
                for (int i = 1; i < xs.Count; i++) e = xs[i] * k + e * (1 - k);
                return e;
            }

            // ATR (Wilder)
            decimal ATR(IReadOnlyList<TickerBar> bars, int p)
            {
                decimal sum = 0m;
                for (int i = bars.Count - p; i < bars.Count; i++)
                {
                    var t = bars[i]; var prev = bars[i - 1];
                    var tr = Math.Max((double)(t.High - t.Low),
                              Math.Max((double)Math.Abs(t.High - prev.Close),
                                       (double)Math.Abs(t.Low - prev.Close)));
                    sum += (decimal)tr;
                }
                return sum / p;
            }

            // Bollinger Bands
            var slice = closes.Skip(closes.Count - _period).ToList();
            var mid = slice.Average();
            var varv = slice.Select(v => (v - mid) * (v - mid)).Average();
            var std = (decimal)Math.Sqrt((double)varv);
            var bbU = mid + _bbMult * std;
            var bbL = mid - _bbMult * std;

            // Keltner Channel
            var ema = EMA(closes, _period);
            var atr = ATR(hist, _period);
            if (atr <= 0) yield break;
            var kcU = ema + _kcMult * atr;
            var kcL = ema - _kcMult * atr;

            // "Squeeze on" when BB inside KC
            bool squeeze = bbU < kcU && bbL > kcL;

            var last = hist[^1];
            var prev = hist[^2];

            if (!squeeze) yield break;

            // --- Confidence model ---
            // How tight is the squeeze? (KC width vs BB width)
            var kcWidth = kcU - kcL;
            var bbWidth = bbU - bbL;
            decimal tightness = kcWidth == 0 ? 0 : 1m - (bbWidth / kcWidth);
            tightness = Math.Clamp(tightness, 0m, 1m);

            // Breakout strength: % move beyond previous high/low
            decimal breakoutStrength = 0m;
            if (last.Close > prev.High) breakoutStrength = (last.Close - prev.High) / prev.High;
            else if (last.Close < prev.Low) breakoutStrength = (prev.Low - last.Close) / prev.Low;
            breakoutStrength = Math.Clamp(breakoutStrength * 10m, 0m, 1m); // scaled, capped

            // Blend into confidence score
            decimal confidence = 0.46m + 0.15m * tightness + 0.15m * breakoutStrength;
            confidence = Math.Clamp(confidence, 0.46m, 0.76m);

            // --- Signals ---
            if (last.Close >= prev.High * (1 + _breakPct))
                yield return new(sym, tf, Name, "BUY", confidence, last.Close,
                    $"BB∈KC→up (tight={tightness:F2}, brk={breakoutStrength:F2})");

            if (last.Close <= prev.Low * (1 - _breakPct))
                yield return new(sym, tf, Name, "SELL", confidence, last.Close,
                    $"BB∈KC→down (tight={tightness:F2}, brk={breakoutStrength:F2})");
        }
    }
    public sealed class BollSqueezePercentile : IPatternDetector
    {
        public string Name => "BollSqueezePct";

        private readonly int _period;          // Bollinger lookback (e.g., 20)
        private readonly int _widthLookback;   // lookback window for width history (e.g., 120)
        private readonly decimal _lowPct;      // low percentile threshold (e.g., 15%)
        private readonly decimal _breakPct;    // breakout buffer vs prev bar (e.g., 0.3%)

        public BollSqueezePercentile(
            int period = 20,
            int widthLookback = 120,
            decimal lowPercentile = 0.15m,
            decimal breakPct = 0.003m)
        {
            _period = period;
            _widthLookback = widthLookback;
            _lowPct = lowPercentile;
            _breakPct = breakPct;
        }

        public IEnumerable<Recommendation> Evaluate(string sym, TimeSpan tf, IReadOnlyList<TickerBar> hist)
        {
            // Need enough bars for BB calc + width history
            if (hist.Count < _period + _widthLookback + 1)
                yield break;

            // --- helper: compute BB width at index ---
            decimal WidthAt(int endIdx)
            {
                var closes = hist.Take(endIdx + 1).Select(h => h.Close).ToArray();
                var slice = closes[(closes.Length - _period)..];
                var mid = slice.Average();
                var varv = slice.Select(v => (v - mid) * (v - mid)).Average();
                var std = (decimal)Math.Sqrt((double)varv);
                return (2m * std) / (mid == 0 ? 1m : mid);
            }

            int lastIdx = hist.Count - 1;

            // --- build width history for percentile bands ---
            var widths = new List<decimal>(_widthLookback);
            for (int i = lastIdx - _widthLookback; i < lastIdx; i++)
                widths.Add(WidthAt(i));

            widths.Sort();
            int loIdx = (int)Math.Clamp(
                (double)Math.Round(_lowPct * (widths.Count - 1)),
                0, widths.Count - 1);
            var lowBand = widths[loIdx];

            // --- current width ---
            var widthNow = WidthAt(lastIdx);

            var last = hist[^1];
            var prev = hist[^2];

            if (widthNow <= lowBand)
            {
                // squeeze severity: narrower relative to low band → higher confidence
                var squeezeTightness = (lowBand == 0 ? 1m : 1m - widthNow / lowBand);
                var baseConf = 0.55m;
                var conf = baseConf + Math.Clamp(squeezeTightness * 0.05m, 0m, 0.05m); // [0.55, 0.60]

                // breakout strength: measure distance beyond buffer
                if (last.Close >= prev.High * (1 + _breakPct))
                {
                    var overshoot = (last.Close - prev.High * (1 + _breakPct)) / prev.High;
                    var boost = Math.Clamp(overshoot * 10m, 0m, 0.03m); // up to +0.03
                    yield return new(sym, tf, Name, "BUY", conf + boost, last.Close,
                        $"pct-sqz≤{_lowPct:P0}");
                }

                if (last.Close <= prev.Low * (1 - _breakPct))
                {
                    var overshoot = (prev.Low * (1 - _breakPct) - last.Close) / prev.Low;
                    var boost = Math.Clamp(overshoot * 10m, 0m, 0.03m);
                    yield return new(sym, tf, Name, "SELL", conf + boost, last.Close,
                        $"pct-sqz≤{_lowPct:P0}");
                }
            }
        }
    }
    public sealed class BollSqueezeRetest : IPatternDetector
    {
        public string Name => "BollSqueezeRetest";

        private readonly int _period;
        private readonly decimal _widthPct;     // threshold for squeeze
        private readonly decimal _retestTolPct; // tolerance around band

        public BollSqueezeRetest(int period = 20, decimal widthPct = 0.012m, decimal retestTolPct = 0.0005m)
        {
            _period = period;
            _widthPct = widthPct;
            _retestTolPct = retestTolPct;
        }

        public IEnumerable<Recommendation> Evaluate(string sym, TimeSpan tf, IReadOnlyList<TickerBar> hist)
        {
            if (hist.Count < _period + 3)
                yield break;

            // Compute BB on bar 'c' (one before last)
            var closesC = hist.Take(hist.Count - 1).Select(h => h.Close).ToArray();
            var sliceC = closesC[(closesC.Length - _period)..];
            var midC = sliceC.Average();
            var varvC = sliceC.Select(v => (v - midC) * (v - midC)).Average();
            var stdC = (decimal)Math.Sqrt((double)varvC);
            var upperC = midC + 2m * stdC;
            var lowerC = midC - 2m * stdC;
            var widthC = (upperC - lowerC) / (midC == 0 ? 1m : midC);

            var a = hist[^3]; // pre-break reference
            var c = hist[^2]; // break bar
            var d = hist[^1]; // retest/confirm

            if (widthC >= _widthPct)
                yield break;

            // Base confidence, then adapt dynamically
            var confBase = 0.55m;
            var tightnessBoost = Math.Clamp((_widthPct - widthC) / _widthPct * 0.05m, 0m, 0.05m); // narrower = stronger

            // --- Upside case ---
            if (c.Close >= a.High && c.Close >= upperC)
            {
                var tol = upperC * _retestTolPct;
                bool retest = d.Low <= upperC + tol && d.Low >= upperC - tol;
                if (retest && d.Close > c.Close)
                {
                    var retestQuality = 1m - Math.Abs(d.Low - upperC) / (upperC == 0 ? 1m : upperC);
                    var retestBoost = Math.Clamp(retestQuality * 0.03m, 0m, 0.03m);

                    var confirmStrength = (d.Close - c.Close) / (c.Close == 0 ? 1m : c.Close);
                    var confirmBoost = Math.Clamp(confirmStrength * 10m, 0m, 0.02m);

                    var conf = confBase + tightnessBoost + retestBoost + confirmBoost;

                    yield return new(sym, tf, Name, "BUY", conf, d.Close, "sqz→up(retest)");
                }
            }

            // --- Downside case ---
            if (c.Close <= a.Low && c.Close <= lowerC)
            {
                var tol = lowerC * _retestTolPct;
                bool retest = d.High >= lowerC - tol && d.High <= lowerC + tol;
                if (retest && d.Close < c.Close)
                {
                    var retestQuality = 1m - Math.Abs(d.High - lowerC) / (lowerC == 0 ? 1m : lowerC);
                    var retestBoost = Math.Clamp(retestQuality * 0.03m, 0m, 0.03m);

                    var confirmStrength = (c.Close - d.Close) / (c.Close == 0 ? 1m : c.Close);
                    var confirmBoost = Math.Clamp(confirmStrength * 10m, 0m, 0.02m);

                    var conf = confBase + tightnessBoost + retestBoost + confirmBoost;

                    yield return new(sym, tf, Name, "SELL", conf, d.Close, "sqz→down(retest)");
                }
            }
        }
    }





    // Uses a 60-second VWAP (on a 1-minute stream) to detect when price
    // has drifted materially away from VWAP and trades *with* that drift.
    // BUY  when close is ≥ _up % above VWAP
    // SELL when close is ≤ _down % below VWAP
    public sealed class VwapDrift : IPatternDetector
    {
        public string Name => "VwapDrift";

        // Thresholds are expressed as fractions (e.g., 0.003m = 0.3%)
        private readonly decimal _up, _down;

        public VwapDrift(decimal up = 0.003m, decimal down = 0.003m)
        {
            _up = up;
            _down = down;
        }

        public IEnumerable<Recommendation> Evaluate(string sym, TimeSpan tf, IReadOnlyList<TickerBar> hist)
        {
            // Only act on 1-minute bars with valid VWAP60s
            var last = hist[^1];
            if (tf != TimeSpan.FromMinutes(1) || last.Vwap60s <= 0)
                yield break;

            var drift = (last.Close - last.Vwap60s) / (last.Vwap60s == 0 ? 1m : last.Vwap60s);

            // Base confidence
            decimal confBase = 0.52m;

            // Confidence grows the more drift exceeds threshold
            decimal DriftConfidence(decimal driftAbs, decimal thresh)
            {
                if (thresh == 0) return 0m;
                var factor = (driftAbs - thresh) / thresh;   // relative overshoot
                return Math.Clamp(factor * 0.06m, 0m, 0.06m); // up to +0.06 boost
            }

            if (drift >= _up)
            {
                var conf = confBase + DriftConfidence(drift, _up);
                yield return new(sym, tf, Name, "BUY", conf, last.Close, $"drift={drift:P2}");
            }

            if (drift <= -_down)
            {
                var conf = confBase + DriftConfidence(-drift, _down);
                yield return new(sym, tf, Name, "SELL", conf, last.Close, $"drift={drift:P2}");
            }
        }
    }
    // Same idea as VwapDrift, but skips micro-range bars / low volume spikes,
    // de-dupes repeated signals, and scales confidence with drift magnitude.
    public sealed class VwapDriftPlus : IPatternDetector
    {
        public string Name => "VwapDrift";
        private readonly decimal _up, _down;          // drift thresholds
        private readonly int _volLookback;            // for avg volume
        private readonly decimal _minVolMult;         // require last.Volume >= avgVol * mult
        private readonly decimal _minRangePct;        // skip bars with tiny range

        public VwapDriftPlus(
            decimal up = 0.003m,
            decimal down = 0.003m,
            int volLookback = 20,
            decimal minVolMult = 0.5m,
            decimal minRangePct = 0.0003m)
        {
            _up = up; _down = down;
            _volLookback = volLookback;
            _minVolMult = minVolMult;
            _minRangePct = minRangePct;
        }

        public IEnumerable<Recommendation> Evaluate(string sym, TimeSpan tf, IReadOnlyList<TickerBar> hist)
        {
            if (tf != TimeSpan.FromMinutes(1) || hist.Count < Math.Max(2, _volLookback + 1)) yield break;

            var last = hist[^1];
            var prev = hist[^2];
            if (last.Vwap60s <= 0 || prev.Vwap60s <= 0) yield break;

            var driftNow = (last.Close - last.Vwap60s) / last.Vwap60s;
            var driftPrev = (prev.Close - prev.Vwap60s) / prev.Vwap60s;

            // Skip micro bars
            var range = last.High - last.Low;
            var refPx = last.Close == 0 ? 1m : last.Close;
            if (range / refPx < _minRangePct) yield break;

            // Volume guard
            var start = hist.Count - _volLookback;
            decimal avgVol = 0m;
            for (int i = start; i < hist.Count; i++) avgVol += hist[i].Volume;
            avgVol /= _volLookback;
            if (avgVol > 0 && last.Volume < avgVol * _minVolMult) yield break;

            // Confidence builder: relative overshoot beyond threshold
            decimal ConfFromDrift(decimal driftAbs, decimal thresh)
            {
                if (thresh == 0) return 0.53m;
                var factor = (driftAbs - thresh) / thresh;
                return Math.Clamp(0.53m + factor * 0.07m, 0.53m, 0.60m);
            }

            // De-dupe: only fire if previous bar wasn’t already beyond threshold
            if (driftNow >= _up && driftPrev < _up)
            {
                var conf = ConfFromDrift(driftNow, _up);
                yield return new(sym, tf, Name, "BUY", conf, last.Close, $"drift={driftNow:P2}");
            }
            else if (driftNow <= -_down && driftPrev > -_down)
            {
                var conf = ConfFromDrift(-driftNow, _down);
                yield return new(sym, tf, Name, "SELL", conf, last.Close, $"drift={driftNow:P2}");
            }
        }
    }
    // Momentum entry on VWAP cross: enter when close crosses from below to above (and vice versa).
    // Add a small epsilon so we don't fire on hairline crosses.
    public sealed class VwapCross : IPatternDetector
    {
        public string Name => "VwapCross";
        private readonly decimal _minDrift; // require at least this normalized distance after the cross

        public VwapCross(decimal minDrift = 0.0003m) { _minDrift = minDrift; }

        public IEnumerable<Recommendation> Evaluate(string sym, TimeSpan tf, IReadOnlyList<TickerBar> hist)
        {
            if (tf != TimeSpan.FromMinutes(1) || hist.Count < 2) yield break;

            var b = hist[^2];
            var c = hist[^1];
            if (b.Vwap60s <= 0 || c.Vwap60s <= 0) yield break;

            var driftPrev = (b.Close - b.Vwap60s) / b.Vwap60s;
            var driftNow = (c.Close - c.Vwap60s) / c.Vwap60s;

            // Confidence helper: scale with overshoot relative to minDrift
            decimal ConfFromDrift(decimal driftAbs, decimal thresh)
            {
                if (thresh <= 0) return 0.54m;
                var factor = (driftAbs - thresh) / thresh;
                return Math.Clamp(0.54m + factor * 0.08m, 0.54m, 0.62m);
            }

            // Up cross: was ≤ 0, now ≥ +min
            if (driftPrev <= 0m && driftNow >= _minDrift)
            {
                var conf = ConfFromDrift(driftNow, _minDrift);
                yield return new(sym, tf, Name, "BUY", conf, c.Close, $"x↑ {driftNow:P2}");
            }

            // Down cross: was ≥ 0, now ≤ -min
            if (driftPrev >= 0m && driftNow <= -_minDrift)
            {
                var conf = ConfFromDrift(-driftNow, _minDrift);
                yield return new(sym, tf, Name, "SELL", conf, c.Close, $"x↓ {driftNow:P2}");
            }
        }
    }
    // Compute z = (close - vwap) standardized by the last N deviations' std dev.
    // Fires when |z| exceeds a threshold (e.g., 2.0). Good for adaptive regimes.
    public sealed class VwapZScoreExtreme : IPatternDetector
    {
        public string Name => "VwapZ";
        private readonly int _n;
        private readonly decimal _zUp, _zDown;

        public VwapZScoreExtreme(int n = 30, decimal zUp = 2.0m, decimal zDown = 2.0m)
        { _n = n; _zUp = zUp; _zDown = zDown; }

        public IEnumerable<Recommendation> Evaluate(string sym, TimeSpan tf, IReadOnlyList<TickerBar> hist)
        {
            if (tf != TimeSpan.FromMinutes(1) || hist.Count < _n + 1) yield break;

            // Build deviations over last N bars where VWAP is valid
            var devs = new List<decimal>(_n);
            for (int i = hist.Count - _n; i < hist.Count; i++)
            {
                if (hist[i].Vwap60s <= 0) yield break;
                devs.Add(hist[i].Close - hist[i].Vwap60s);
            }
            var mean = devs.Average();
            decimal varv = 0m;
            foreach (var d in devs) { var t = d - mean; varv += t * t; }
            varv /= devs.Count;
            var std = (decimal)Math.Sqrt((double)varv);
            if (std == 0) yield break;

            var last = hist[^1];
            var z = (last.Close - last.Vwap60s - mean) / std;

            // Confidence grows with overshoot beyond threshold
            decimal ConfFromZ(decimal zAbs, decimal thresh)
            {
                if (thresh <= 0) return 0.56m;
                var factor = (zAbs - thresh) / thresh; // overshoot ratio
                return Math.Clamp(0.56m + factor * 0.08m, 0.56m, 0.64m);
            }

            if (z >= _zUp)
            {
                var conf = ConfFromZ(z, _zUp);
                yield return new(sym, tf, Name, "BUY", conf, last.Close, $"z={z:F2}");
            }
            else if (z <= -_zDown)
            {
                var conf = ConfFromZ(-z, _zDown);
                yield return new(sym, tf, Name, "SELL", conf, last.Close, $"z={z:F2}");
            }
        }
    }
    // Contrarian: after stretching beyond a drift threshold, wait for a cross BACK through VWAP,
    // then trade toward mean reversion. Cuts a lot of knife-catching.
    public sealed class VwapReversionCrossback : IPatternDetector
    {
        public string Name => "VwapRevert";
        private readonly decimal _th; // stretch threshold before we allow a fade

        public VwapReversionCrossback(decimal threshold = 0.004m) { _th = threshold; }

        public IEnumerable<Recommendation> Evaluate(string sym, TimeSpan tf, IReadOnlyList<TickerBar> hist)
        {
            if (tf != TimeSpan.FromMinutes(1) || hist.Count < 2) yield break;

            var b = hist[^2];
            var c = hist[^1];
            if (b.Vwap60s <= 0 || c.Vwap60s <= 0) yield break;

            var driftPrev = (b.Close - b.Vwap60s) / b.Vwap60s;
            var driftNow = (c.Close - c.Vwap60s) / c.Vwap60s;

            // Confidence: grows with how far driftPrev stretched beyond threshold
            decimal ConfFromStretch(decimal driftAbs)
            {
                if (_th <= 0) return 0.55m;
                var factor = (driftAbs - _th) / _th; // overshoot ratio
                return Math.Clamp(0.55m + factor * 0.08m, 0.55m, 0.63m);
            }

            // SELL fade: prev stretched UP, now crossed back BELOW VWAP
            if (driftPrev >= _th && driftNow <= 0m)
            {
                var conf = ConfFromStretch(driftPrev);
                yield return new(sym, tf, Name, "SELL", conf, c.Close, "revert↓");
            }

            // BUY fade: prev stretched DOWN, now crossed back ABOVE VWAP
            if (driftPrev <= -_th && driftNow >= 0m)
            {
                var conf = ConfFromStretch(-driftPrev);
                yield return new(sym, tf, Name, "BUY", conf, c.Close, "revert↑");
            }
        }
    }
    // Trade with trend: only take a bounce off VWAP in the trend direction.
    // Uses a slow EMA slope as trend proxy; requires a "touch" near VWAP.
    public sealed class VwapTrendPullback : IPatternDetector
    {
        public string Name => "VwapTrendPB";
        private readonly int _ema;
        private readonly decimal _touchTol; // max |drift| to count as a touch

        public VwapTrendPullback(int emaPeriod = 50, decimal touchTolerance = 0.0005m)
        { _ema = emaPeriod; _touchTol = touchTolerance; }

        public IEnumerable<Recommendation> Evaluate(string sym, TimeSpan tf, IReadOnlyList<TickerBar> hist)
        {
            if (tf != TimeSpan.FromMinutes(1) || hist.Count < _ema + 2) yield break;

            var b = hist[^2];
            var c = hist[^1];
            if (b.Vwap60s <= 0 || c.Vwap60s <= 0) yield break;

            // EMA on closes
            var closes = hist.Select(h => h.Close).ToList();
            decimal EMA(IReadOnlyList<decimal> xs, int p)
            { var k = 2m / (p + 1); decimal e = xs[0]; for (int i = 1; i < xs.Count; i++) e = xs[i] * k + e * (1 - k); return e; }
            var emaNow = EMA(closes, _ema);
            var emaPrev = EMA(closes.Take(closes.Count - 1).ToList(), _ema);

            var driftPrev = (b.Close - b.Vwap60s) / b.Vwap60s;
            var driftNow = (c.Close - c.Vwap60s) / c.Vwap60s;

            // Confidence scaling
            decimal ConfFromTrendAndBounce(decimal emaSlope, decimal bouncePct)
            {
                // baseline 0.57
                // add up to +0.04 from EMA slope strength
                // add up to +0.03 from bounce magnitude
                var slopeBoost = Math.Clamp((emaSlope / (emaPrev == 0 ? 1 : emaPrev)) * 100m, -0.05m, 0.05m);
                var conf = 0.57m + slopeBoost * 0.8m + Math.Clamp(bouncePct * 5m, 0m, 0.03m);
                return Math.Clamp(conf, 0.57m, 0.64m);
            }

            // Uptrend
            if (emaNow > emaPrev && c.Close > emaNow && Math.Abs(driftPrev) <= _touchTol && c.Close > b.Close)
            {
                var bounce = (c.Close - b.Close) / (b.Close == 0 ? 1 : b.Close);
                var conf = ConfFromTrendAndBounce(emaNow - emaPrev, bounce);
                yield return new(sym, tf, Name, "BUY", conf, c.Close, "pb→up");
            }

            // Downtrend
            if (emaNow < emaPrev && c.Close < emaNow && Math.Abs(driftPrev) <= _touchTol && c.Close < b.Close)
            {
                var bounce = (b.Close - c.Close) / (b.Close == 0 ? 1 : b.Close);
                var conf = ConfFromTrendAndBounce(emaPrev - emaNow, bounce);
                yield return new(sym, tf, Name, "SELL", conf, c.Close, "pb→down");
            }
        }
    }
    // Approximate multi-window VWAP with bar data: compute VWAP over the last Ns and Nm bars
    // using typical price * volume. Signal only when price aligns on both windows.
    public sealed class VwapMultiWindowAlign : IPatternDetector
    {
        public string Name => "VwapMultiVWAP";
        private readonly int _shortBars, _midBars;

        public VwapMultiWindowAlign(int shortBars = 20, int midBars = 60)
        { _shortBars = shortBars; _midBars = midBars; }

        public IEnumerable<Recommendation> Evaluate(string sym, TimeSpan tf, IReadOnlyList<TickerBar> hist)
        {
            if (hist.Count < Math.Max(_shortBars, _midBars)) yield break;

            decimal VwapBars(int n)
            {
                int start = hist.Count - n;
                decimal pv = 0m, v = 0m;
                for (int i = start; i < hist.Count; i++)
                {
                    var tp = (hist[i].High + hist[i].Low + hist[i].Close) / 3m; // typical price
                    pv += tp * hist[i].Volume;
                    v += hist[i].Volume;
                }
                return v == 0 ? 0 : pv / v;
            }

            var last = hist[^1];
            var vS = VwapBars(_shortBars);
            var vM = VwapBars(_midBars);
            if (vS <= 0 || vM <= 0) yield break;

            // Confidence function: base 0.55, add from alignment strength
            decimal Conf(decimal close, decimal vs, decimal vm)
            {
                // normalized distances
                var dS = Math.Abs((close - vs) / vs);
                var dM = Math.Abs((close - vm) / vm);
                var avgDist = (dS + dM) / 2m;

                // VWAPs agreement (closer = more reliable)
                var align = 1m - Math.Abs(vS - vM) / ((vS + vM) / 2m);

                // Confidence scaling:
                // + up to 0.04 if price is clearly away from both VWAPs
                // + up to 0.03 if VWAPs are closely aligned
                var conf = 0.55m + Math.Clamp(avgDist * 10m, 0m, 0.04m) + Math.Clamp(align * 0.03m, 0m, 0.03m);
                return Math.Clamp(conf, 0.55m, 0.62m);
            }

            if (last.Close > vS && last.Close > vM)
            {
                var conf = Conf(last.Close, vS, vM);
                yield return new(sym, tf, Name, "BUY", conf, last.Close, $"vwapS={vS:F4},vwapM={vM:F4}");

            }
        }
        // =====================
        // New detectors
        // =====================
    }
    public sealed class DonchianBreakout : IPatternDetector
    {
        public string Name => "DonchianBreakout";
        private readonly int _lookback;
        private readonly decimal _bufferPct;

        public DonchianBreakout(int lookback = 20, decimal bufferPct = 0.0m)
        {
            _lookback = lookback;
            _bufferPct = bufferPct;
        }

        public IEnumerable<Recommendation> Evaluate(string sym, TimeSpan tf, IReadOnlyList<TickerBar> hist)
        {
            if (hist.Count < _lookback + 2) yield break;

            // Lookback window (exclude the last bar for decision)
            var window = hist.Skip(hist.Count - _lookback - 1).Take(_lookback).ToList();
            var hh = window.Max(b => b.High);
            var ll = window.Min(b => b.Low);

            var last = hist[^1];
            var up = hh * (1 + _bufferPct);
            var dn = ll * (1 - _bufferPct);

            // Channel range as context
            var range = hh - ll;
            var mid = (hh + ll) / 2m;
            if (mid <= 0) yield break;

            decimal Conf(decimal close, bool isUp)
            {
                // breakout distance (normalized vs channel size)
                var dist = isUp
                    ? (close - up) / (mid == 0 ? 1 : mid)
                    : (dn - close) / (mid == 0 ? 1 : mid);

                // channel width (narrower = weaker)
                var widthNorm = range / mid;

                // Base = 0.56, add up to +0.05 from breakout distance, +0.03 from wide channel
                var conf = 0.56m + Math.Clamp(dist * 5m, 0m, 0.05m) + Math.Clamp(widthNorm * 0.03m, 0m, 0.03m);
                return Math.Clamp(conf, 0.56m, 0.64m);
            }

            if (last.Close >= up)
            {
                var conf = Conf(last.Close, true);
                yield return new(sym, tf, Name, "BUY", conf, last.Close, $">{_lookback}H");
            }
            else if (last.Close <= dn)
            {
                var conf = Conf(last.Close, false);
                yield return new(sym, tf, Name, "SELL", conf, last.Close, $"<{_lookback}L");
            }
        }
    }

    public sealed class AtrBreakout : IPatternDetector
    {
        public string Name => "AtrBreakout";
        private readonly int _n;
        private readonly decimal _k;

        public AtrBreakout(int n = 14, decimal k = 1.5m)
        {
            _n = n;
            _k = k;
        }

        public IEnumerable<Recommendation> Evaluate(string sym, TimeSpan tf, IReadOnlyList<TickerBar> hist)
        {
            if (hist.Count < _n + 2) yield break;

            var atr = TA.ATR(hist, _n);
            if (atr <= 0) yield break;

            var last = hist[^1];
            var prev = hist[^2];
            var refPx = last.Close == 0 ? 1m : last.Close;

            decimal Conf(decimal breakoutDist)
            {
                // Normalized distance vs ATR
                var strength = breakoutDist / atr;

                // ATR relative to price (bigger ATR = more significant)
                var atrNorm = atr / refPx;

                // Base 0.55, add up to +0.05 from breakout strength, +0.03 from ATR significance
                var conf = 0.55m
                           + Math.Clamp((strength - _k) * 0.02m, 0m, 0.05m)
                           + Math.Clamp(atrNorm * 2m, 0m, 0.03m);

                return Math.Clamp(conf, 0.55m, 0.63m);
            }

            if (last.Close >= prev.High + _k * atr)
            {
                var dist = last.Close - (prev.High + _k * atr);
                yield return new(sym, tf, Name, "BUY", Conf(dist), last.Close, $"atr={atr:F4}");
            }
            else if (last.Close <= prev.Low - _k * atr)
            {
                var dist = (prev.Low - _k * atr) - last.Close;
                yield return new(sym, tf, Name, "SELL", Conf(dist), last.Close, $"atr={atr:F4}");
            }
        }
    }

    public sealed class MacdCross : IPatternDetector
    {
        public string Name => "MacdCross";
        private readonly int _fast, _slow, _signal;

        public MacdCross(int fast = 12, int slow = 26, int signal = 9)
        {
            _fast = fast; _slow = slow; _signal = signal;
        }

        public IEnumerable<Recommendation> Evaluate(string sym, TimeSpan tf, IReadOnlyList<TickerBar> hist)
        {
            int need = Math.Max(_slow + _signal + 2, 40);
            if (hist.Count < need) yield break;

            var closes = hist.Select(h => h.Close).ToList();

            // EMAs
            decimal kf = 2m / (_fast + 1);
            decimal ks = 2m / (_slow + 1);
            decimal ksx = 2m / (_signal + 1);

            decimal emaF = closes[0];
            decimal emaS = closes[0];

            var macdSeries = new List<decimal>(closes.Count);
            for (int i = 0; i < closes.Count; i++)
            {
                var c = closes[i];
                emaF = i == 0 ? c : c * kf + emaF * (1 - kf);
                emaS = i == 0 ? c : c * ks + emaS * (1 - ks);
                macdSeries.Add(emaF - emaS);
            }

            decimal sig = macdSeries[0];
            var signalSeries = new List<decimal>(macdSeries.Count);
            for (int i = 0; i < macdSeries.Count; i++)
            {
                var m = macdSeries[i];
                sig = i == 0 ? m : m * ksx + sig * (1 - ksx);
                signalSeries.Add(sig);
            }

            var macdPrev = macdSeries[^2] - signalSeries[^2];
            var macdNow = macdSeries[^1] - signalSeries[^1];
            var histPrev = macdSeries[^2] - macdSeries[^3]; // slope proxy
            var histNow = macdSeries[^1] - macdSeries[^2];

            var last = hist[^1];

            decimal Conf(decimal dist, decimal slopeNow)
            {
                // Normalize distance magnitude
                var mag = Math.Clamp(Math.Abs(dist) * 3m, 0m, 0.05m);

                // Slope adds conviction if in the same direction
                var slopeBoost = slopeNow * dist > 0 ? 0.02m : 0m;

                return Math.Clamp(0.53m + mag + slopeBoost, 0.53m, 0.60m);
            }

            // Cross up
            if (macdPrev <= 0 && macdNow > 0)
            {
                yield return new(sym, tf, Name, "BUY", Conf(macdNow, histNow), last.Close, "macd>sig");
            }

            // Cross down
            if (macdPrev >= 0 && macdNow < 0)
            {
                yield return new(sym, tf, Name, "SELL", Conf(macdNow, histNow), last.Close, "macd<sig");
            }
        }
    }

    public sealed class StochasticCross : IPatternDetector
    {
        public string Name => "StochasticCross";
        private readonly int _kPeriod, _dPeriod;
        private readonly decimal _os, _ob;

        public StochasticCross(int kPeriod = 14, int dPeriod = 3, decimal oversold = 20m, decimal overbought = 80m)
        {
            _kPeriod = kPeriod; _dPeriod = dPeriod;
            _os = oversold; _ob = overbought;
        }

        public IEnumerable<Recommendation> Evaluate(string sym, TimeSpan tf, IReadOnlyList<TickerBar> hist)
        {
            if (hist.Count < _kPeriod + _dPeriod + 2) yield break;

            decimal K(int idx)
            {
                int start = Math.Max(0, idx - _kPeriod + 1);
                var slice = hist.Skip(start).Take(idx - start + 1).ToList();
                var hh = slice.Max(b => b.High);
                var ll = slice.Min(b => b.Low);
                var close = hist[idx].Close;
                var denom = hh - ll;
                if (denom == 0) return 50m;
                return 100m * (close - ll) / denom;
            }

            // compute K values
            var kPrev = K(hist.Count - 2);
            var kNow = K(hist.Count - 1);

            // D as SMA of last _dPeriod K values
            decimal D(int idx)
            {
                var ks = new List<decimal>();
                for (int i = idx - _dPeriod + 1; i <= idx; i++) ks.Add(K(i));
                return ks.Average();
            }
            var dPrev = D(hist.Count - 2);
            var dNow = D(hist.Count - 1);

            var last = hist[^1];

            // Confidence function
            decimal Conf(decimal kNow, decimal dNow, decimal kPrev, decimal dPrev, bool bullish)
            {
                var baseConf = 0.51m;
                var dist = Math.Abs(kNow - dNow) / 100m;               // normalize
                var distBoost = Math.Clamp(dist * 0.5m, 0m, 0.05m);    // up to +0.05

                var zoneBoost = 0m;
                if (bullish && kNow <= _os) zoneBoost = 0.03m;
                if (!bullish && kNow >= _ob) zoneBoost = 0.03m;

                var momBoost = ((kNow - kPrev) * (bullish ? 1 : -1)) > 0 ? 0.02m : 0m;

                return Math.Clamp(baseConf + distBoost + zoneBoost + momBoost, baseConf, 0.60m);
            }

            // Signals
            if (kPrev <= dPrev && kNow > dNow && kNow < _ob)
                yield return new(sym, tf, Name, "BUY", Conf(kNow, dNow, kPrev, dPrev, true), last.Close, $"Kx>D ({kNow:F1})");

            if (kPrev >= dPrev && kNow < dNow && kNow > _os)
                yield return new(sym, tf, Name, "SELL", Conf(kNow, dNow, kPrev, dPrev, false), last.Close, $"Kx<D ({kNow:F1})");

            if (kNow <= _os && kPrev < _os && kNow > dNow)
                yield return new(sym, tf, Name, "BUY", Conf(kNow, dNow, kPrev, dPrev, true), last.Close, $"K<{_os}");

            if (kNow >= _ob && kPrev > _ob && kNow < dNow)
                yield return new(sym, tf, Name, "SELL", Conf(kNow, dNow, kPrev, dPrev, false), last.Close, $"K>{_ob}");
        }
    }

    public sealed class Engulfing : IPatternDetector
    {
        public string Name => "Engulfing";

        public IEnumerable<Recommendation> Evaluate(string sym, TimeSpan tf, IReadOnlyList<TickerBar> hist)
        {
            if (hist.Count < 2) yield break;

            var a = hist[^2];
            var b = hist[^1];

            var aBull = a.Close > a.Open;
            var aBear = a.Close < a.Open;
            var bBull = b.Close > b.Open;
            var bBear = b.Close < b.Open;

            var aBody = Math.Abs(a.Close - a.Open);
            var bBody = Math.Abs(b.Close - b.Open);

            var minBody = (a.High - a.Low) * 0.15m; // avoid tiny bodies

            // Confidence helper
            decimal Conf(decimal aBody, decimal bBody, bool bullish)
            {
                var baseConf = 0.52m;

                // Bigger engulf bar relative to prior body → more confidence
                var bodyRatio = bBody / (aBody == 0 ? 1 : aBody);
                var sizeBoost = Math.Clamp(bodyRatio * 0.02m, 0m, 0.05m);

                // Extra if full engulf extends beyond body (close vs open crossover)
                var wickEngulf = bullish
                    ? (b.Close - a.Open) / (a.High - a.Low == 0 ? 1 : a.High - a.Low)
                    : (a.Open - b.Close) / (a.High - a.Low == 0 ? 1 : a.High - a.Low);
                var wickBoost = Math.Clamp(wickEngulf * 0.03m, 0m, 0.03m);

                // Range bonus: bigger relative candle
                var rangeRatio = (b.High - b.Low) / ((a.High - a.Low) == 0 ? 1 : (a.High - a.Low));
                var rangeBoost = Math.Clamp((rangeRatio - 1m) * 0.01m, 0m, 0.03m);

                return Math.Clamp(baseConf + sizeBoost + wickBoost + rangeBoost, baseConf, 0.60m);
            }

            // Bullish engulf
            if (bBull && aBear && b.Open <= a.Close && b.Close >= a.Open && bBody > minBody)
            {
                var conf = Conf(aBody, bBody, true);
                yield return new(sym, tf, Name, "BUY", conf, b.Close, "bull-engulf");
            }

            // Bearish engulf
            if (bBear && aBull && b.Open >= a.Close && b.Close <= a.Open && bBody > minBody)
            {
                var conf = Conf(aBody, bBody, false);
                yield return new(sym, tf, Name, "SELL", conf, b.Close, "bear-engulf");
            }
        }
    }

    public sealed class DojiReversal : IPatternDetector
    {
        public string Name => "DojiReversal";

        public IEnumerable<Recommendation> Evaluate(string sym, TimeSpan tf, IReadOnlyList<TickerBar> hist)
        {
            if (hist.Count < 5) yield break;

            var b = hist[^1]; // doji candidate
            var a = hist[^2];

            var range = b.High - b.Low;
            if (range <= 0) yield break;

            var body = Math.Abs(b.Close - b.Open);
            var isDoji = body / (range == 0 ? 1 : range) <= 0.1m;
            if (!isDoji) yield break;

            bool downTrend = hist[^2].Close < hist[^3].Close && hist[^3].Close < hist[^4].Close;
            bool upTrend = hist[^2].Close > hist[^3].Close && hist[^3].Close > hist[^4].Close;

            // Confidence helper
            decimal Conf(bool bullish, IReadOnlyList<TickerBar> h, TickerBar doji, decimal body, decimal range)
            {
                decimal baseConf = 0.50m;

                // 1) Trend strength: size of 3-bar move vs avg range
                var trendMove = Math.Abs(h[^2].Close - h[^4].Close);
                var avgRange = (h[^2].High - h[^2].Low + h[^3].High - h[^3].Low + h[^4].High - h[^4].Low) / 3m;
                var trendBoost = Math.Clamp(trendMove / (avgRange == 0 ? 1 : avgRange) * 0.02m, 0m, 0.04m);

                // 2) Doji clarity: smaller body → higher confidence
                var clarity = 1m - (body / (range == 0 ? 1 : range));
                var clarityBoost = Math.Clamp(clarity * 0.05m, 0m, 0.05m);

                // 3) Doji range vs previous bar: if bigger, add small bump
                var prevRange = h[^2].High - h[^2].Low;
                var rangeBoost = Math.Clamp((range - prevRange) / (prevRange == 0 ? 1 : prevRange) * 0.02m, 0m, 0.03m);

                return Math.Clamp(baseConf + trendBoost + clarityBoost + rangeBoost, baseConf, 0.60m);
            }

            if (downTrend && b.Close > b.Open) // bullish reversal
            {
                var conf = Conf(true, hist, b, body, range);
                yield return new(sym, tf, Name, "BUY", conf, b.Close, "doji-down");
            }

            if (upTrend && b.Close < b.Open) // bearish reversal
            {
                var conf = Conf(false, hist, b, body, range);
                yield return new(sym, tf, Name, "SELL", conf, b.Close, "doji-up");
            }
        }
    }

    public sealed class Nr7Breakout : IPatternDetector
    {
        public string Name => "NR7Breakout";

        public IEnumerable<Recommendation> Evaluate(string sym, TimeSpan tf, IReadOnlyList<TickerBar> hist)
        {
            if (hist.Count < 8) yield break;

            var prev7 = hist.Skip(hist.Count - 8).Take(7).ToList(); // includes the penultimate bar
            var ranges = prev7.Select(b => b.High - b.Low).ToList();

            // find narrowest bar among the 7
            var minIdx = 0; decimal minR = ranges[0];
            for (int i = 1; i < ranges.Count; i++)
                if (ranges[i] < minR) { minR = ranges[i]; minIdx = i; }

            // Require the penultimate bar to be the NR7
            if (minIdx != prev7.Count - 1) yield break;

            var narrow = hist[^2]; // the NR7 bar
            var last = hist[^1];   // breakout bar

            // Confidence helper
            decimal Conf(TickerBar nr7, TickerBar breakout, IReadOnlyList<TickerBar> h)
            {
                decimal baseConf = 0.54m;

                // 1) Compression strength: NR7 vs avg of previous 6 ranges
                var avg6 = ranges.Take(ranges.Count - 1).Average();
                var compression = avg6 == 0 ? 0 : avg6 / (nr7.High - nr7.Low);
                var compBoost = Math.Clamp((compression - 1m) * 0.02m, 0m, 0.04m);

                // 2) Breakout volume vs 10-bar average
                var avgVol = h.Skip(Math.Max(0, h.Count - 11)).Take(10).Average(x => x.Volume);
                var volBoost = (avgVol > 0 && breakout.Volume > avgVol)
                    ? Math.Clamp((breakout.Volume / avgVol - 1m) * 0.02m, 0m, 0.04m)
                    : 0m;

                // 3) Breakout range size vs 10-bar avg
                var avgRange = h.Skip(Math.Max(0, h.Count - 11)).Take(10).Average(x => x.High - x.Low);
                var brRange = breakout.High - breakout.Low;
                var rangeBoost = (avgRange > 0 && brRange > avgRange)
                    ? Math.Clamp((brRange / avgRange - 1m) * 0.02m, 0m, 0.03m)
                    : 0m;

                return Math.Clamp(baseConf + compBoost + volBoost + rangeBoost, baseConf, 0.60m);
            }

            if (last.Close > narrow.High)
            {
                var conf = Conf(narrow, last, hist);
                yield return new(sym, tf, Name, "BUY", conf, last.Close, "breakNR7H");
            }

            if (last.Close < narrow.Low)
            {
                var conf = Conf(narrow, last, hist);
                yield return new(sym, tf, Name, "SELL", conf, last.Close, "breakNR7L");
            }
        }
    }

    public sealed class KeltnerSqueezeBreakout : IPatternDetector
    {
        public string Name => "KeltnerSqueeze";
        private readonly int _period;
        private readonly decimal _mult;
        private readonly decimal _breakPct;

        public KeltnerSqueezeBreakout(int period = 20, decimal mult = 2m, decimal breakPct = 0.003m)
        { _period = period; _mult = mult; _breakPct = breakPct; }

        public IEnumerable<Recommendation> Evaluate(string sym, TimeSpan tf, IReadOnlyList<TickerBar> hist)
        {
            if (hist.Count < _period + 2) yield break;

            var closes = hist.Select(h => h.Close).ToList();
            var slice = closes.Skip(closes.Count - _period).ToList();
            var mid = slice.Average();
            var std = TA.StdDev(slice);
            var bbU = mid + 2m * std;
            var bbL = mid - 2m * std;

            var atr = TA.ATR(hist, _period);
            if (atr <= 0) yield break;

            var ema = TA.EMA(closes, _period);
            var kcU = ema + _mult * atr;
            var kcL = ema - _mult * atr;

            bool squeeze = bbU < kcU && bbL > kcL;
            var last = hist[^1];
            var prev = hist[^2];

            decimal Conf(decimal bbU, decimal bbL, decimal kcU, decimal kcL, TickerBar prev, TickerBar last)
            {
                decimal baseConf = 0.56m;

                // 1) Squeeze tightness: distance between KC and BB
                var kcWidth = kcU - kcL;
                var bbWidth = bbU - bbL;
                var tightness = kcWidth == 0 ? 0 : (kcWidth - bbWidth) / kcWidth;
                var tightBoost = Math.Clamp(tightness * 0.05m, 0m, 0.03m);

                // 2) Break strength: how far beyond breakout threshold
                decimal upBreak = last.Close / (prev.High * (1 + _breakPct)) - 1m;
                decimal dnBreak = (prev.Low * (1 - _breakPct)) / last.Close - 1m;
                var breakBoost = Math.Clamp(Math.Max(upBreak, dnBreak) * 10m, 0m, 0.03m);

                // 3) Volume confirmation: compare to 20-bar average
                var avgVol = hist.Skip(Math.Max(0, hist.Count - 21)).Take(20).Average(b => b.Volume);
                var volBoost = (avgVol > 0 && last.Volume > avgVol)
                    ? Math.Clamp((last.Volume / avgVol - 1m) * 0.01m, 0m, 0.03m)
                    : 0m;

                return Math.Clamp(baseConf + tightBoost + breakBoost + volBoost, baseConf, 0.62m);
            }

            if (squeeze && last.Close >= prev.High * (1 + _breakPct))
            {
                var conf = Conf(bbU, bbL, kcU, kcL, prev, last);
                yield return new(sym, tf, Name, "BUY", conf, last.Close, "squeeze-up");
            }

            if (squeeze && last.Close <= prev.Low * (1 - _breakPct))
            {
                var conf = Conf(bbU, bbL, kcU, kcL, prev, last);
                yield return new(sym, tf, Name, "SELL", conf, last.Close, "squeeze-dn");
            }
        }
    }

    public sealed class PullbackToEma : IPatternDetector
    {
        public string Name => "PullbackToEMA";
        private readonly int _period;
        private readonly decimal _touchTol;

        public PullbackToEma(int period = 20, decimal touchTol = 0.001m)
        {
            _period = period;
            _touchTol = touchTol;
        }

        public IEnumerable<Recommendation> Evaluate(string sym, TimeSpan tf, IReadOnlyList<TickerBar> hist)
        {
            if (hist.Count < _period + 3) yield break;

            var closes = hist.Select(h => h.Close).ToList();
            var emaNow = TA.EMA(closes, _period);
            var emaPrev = TA.EMA(closes.Take(closes.Count - 1).ToList(), _period);

            var last = hist[^1];
            var prev = hist[^2];

            var upTrend = last.Close > emaNow && emaNow > emaPrev;
            var dnTrend = last.Close < emaNow && emaNow < emaPrev;

            bool touched = Math.Abs((last.Low + last.High) / 2m - emaNow) / (emaNow == 0 ? 1 : emaNow) <= _touchTol
                           || (last.Low <= emaNow && last.High >= emaNow);

            decimal Confidence(bool isUp)
            {
                decimal baseConf = 0.54m;

                // 1) Trend slope strength
                var slope = Math.Abs((emaNow - emaPrev) / (emaPrev == 0 ? 1 : emaPrev));
                var slopeBoost = Math.Clamp(slope * 20m, 0m, 0.03m);

                // 2) Touch quality (closer = stronger)
                var dist = Math.Abs(last.Close - emaNow) / (emaNow == 0 ? 1 : emaNow);
                var touchBoost = Math.Clamp(((_touchTol - dist) / _touchTol) * 0.02m, 0m, 0.02m);

                // 3) Break strength
                decimal breakStrength = isUp
                    ? (last.Close - prev.High) / (prev.High == 0 ? 1 : prev.High)
                    : (prev.Low - last.Close) / (prev.Low == 0 ? 1 : prev.Low);
                var breakBoost = Math.Clamp(breakStrength * 10m, 0m, 0.03m);

                // 4) Volume confirmation
                var avgVol = hist.Skip(Math.Max(0, hist.Count - 21)).Take(20).Average(b => b.Volume);
                var volBoost = (avgVol > 0 && last.Volume > avgVol)
                    ? Math.Clamp((last.Volume / avgVol - 1m) * 0.01m, 0m, 0.02m)
                    : 0m;

                return Math.Clamp(baseConf + slopeBoost + touchBoost + breakBoost + volBoost, baseConf, 0.62m);
            }

            if (upTrend && touched && last.Close > prev.High)
                yield return new(sym, tf, Name, "BUY", Confidence(true), last.Close, "pbk-ema");

            if (dnTrend && touched && last.Close < prev.Low)
                yield return new(sym, tf, Name, "SELL", Confidence(false), last.Close, "pbk-ema");
        }
    }

    public sealed class ObvBreakout : IPatternDetector
    {
        public string Name => "OBVBreakout";
        private readonly int _lookback;

        public ObvBreakout(int lookback = 50)
        {
            _lookback = lookback;
        }

        public IEnumerable<Recommendation> Evaluate(string sym, TimeSpan tf, IReadOnlyList<TickerBar> hist)
        {
            if (hist.Count < _lookback + 2) yield break;

            // OBV calculation
            decimal obv = 0m;
            var obvSeries = new List<decimal>(hist.Count) { 0m };
            for (int i = 1; i < hist.Count; i++)
            {
                var d = hist[i].Close - hist[i - 1].Close;
                if (d > 0) obv += hist[i].Volume;
                else if (d < 0) obv -= hist[i].Volume;
                obvSeries.Add(obv);
            }

            var lastObv = obvSeries[^1];
            var window = obvSeries.Skip(obvSeries.Count - _lookback - 1).Take(_lookback).ToList();
            var obvH = window.Max();
            var obvL = window.Min();
            var last = hist[^1];

            decimal Confidence(bool isUp, decimal breakoutStrength)
            {
                decimal baseConf = 0.52m;

                // 1) Break strength of OBV
                var breakBoost = Math.Clamp(breakoutStrength * 5m, 0m, 0.03m);

                // 2) Volume confirmation
                var avgVol = hist.Skip(Math.Max(0, hist.Count - 21)).Take(20).Average(b => b.Volume);
                var volBoost = (avgVol > 0 && last.Volume > avgVol)
                    ? Math.Clamp((last.Volume / avgVol - 1m) * 0.01m, 0m, 0.02m)
                    : 0m;

                // 3) Trend agreement: price also breaking local high/low
                var priceWindow = hist.Skip(hist.Count - _lookback - 1).Take(_lookback).ToList();
                var pxHigh = priceWindow.Max(b => b.High);
                var pxLow = priceWindow.Min(b => b.Low);

                decimal trendBoost = 0m;
                if (isUp && last.Close > pxHigh) trendBoost = 0.02m;
                if (!isUp && last.Close < pxLow) trendBoost = 0.02m;

                return Math.Clamp(baseConf + breakBoost + volBoost + trendBoost, baseConf, 0.59m);
            }

            if (lastObv > obvH)
            {
                var breakoutStrength = (lastObv - obvH) / (obvH == 0 ? 1 : Math.Abs(obvH));
                yield return new(sym, tf, Name, "BUY", Confidence(true, breakoutStrength), last.Close, "obv>H");
            }
            if (lastObv < obvL)
            {
                var breakoutStrength = (obvL - lastObv) / (obvL == 0 ? 1 : Math.Abs(obvL));
                yield return new(sym, tf, Name, "SELL", Confidence(false, breakoutStrength), last.Close, "obv<L");
            }
        }
    }

    public sealed class VolumeSpike : IPatternDetector
    {
        public string Name => "VolumeSpike";
        private readonly int _period;
        private readonly decimal _mult;

        public VolumeSpike(int period = 20, decimal mult = 2.5m)
        {
            _period = period;
            _mult = mult;
        }

        public IEnumerable<Recommendation> Evaluate(string sym, TimeSpan tf, IReadOnlyList<TickerBar> hist)
        {
            if (hist.Count < _period + 2) yield break;

            var vols = hist.Skip(hist.Count - _period - 1).Take(_period).Select(b => b.Volume).ToList();
            var avgVol = vols.Average();
            if (avgVol <= 0) yield break;

            var last = hist[^1];
            var prev = hist[^2];

            if (last.Volume >= avgVol * _mult)
            {
                // Position of close within bar (0=low, 1=high)
                var posClose = (last.Close - last.Low) / ((last.High - last.Low) == 0 ? 1 : (last.High - last.Low));

                decimal Confidence(bool isBuy)
                {
                    decimal baseConf = 0.52m;

                    // 1) Volume multiple boost
                    var volMult = last.Volume / avgVol;
                    var volBoost = Math.Clamp((volMult - _mult) * 0.01m, 0m, 0.03m);

                    // 2) Bar close strength boost
                    var closeBoost = 0m;
                    if (isBuy) closeBoost = Math.Clamp((posClose - 0.7m) * 0.1m, 0m, 0.02m);
                    else closeBoost = Math.Clamp((0.3m - posClose) * 0.1m, 0m, 0.02m);

                    // 3) Follow-through confirmation
                    var followBoost = 0m;
                    if (isBuy && last.Close > prev.Close) followBoost = 0.02m;
                    if (!isBuy && last.Close < prev.Close) followBoost = 0.02m;

                    return Math.Clamp(baseConf + volBoost + closeBoost + followBoost, baseConf, 0.59m);
                }

                if (posClose >= 0.7m && last.Close > prev.Close)
                    yield return new(sym, tf, Name, "BUY", Confidence(true), last.Close, "vol-x");

                if (posClose <= 0.3m && last.Close < prev.Close)
                    yield return new(sym, tf, Name, "SELL", Confidence(false), last.Close, "vol-x");
            }
        }
    }

    public sealed class FractalBreakout : IPatternDetector
    {
        public string Name => "FractalBreakout";

        public IEnumerable<Recommendation> Evaluate(string sym, TimeSpan tf, IReadOnlyList<TickerBar> hist)
        {
            // Bill Williams 5-bar fractal confirmation on the following bar
            if (hist.Count < 6) yield break;

            var last = hist[^1];
            // Use bar -2 as potential fractal (completed)
            var f = hist[^3];
            var fL1 = hist[^4];
            var fL2 = hist[^5];
            var fR1 = hist[^2];
            var fR2 = hist[^1];

            bool upFractal = f.High > fL1.High && f.High > fL2.High && f.High > fR1.High && f.High > fR2.High;
            bool dnFractal = f.Low < fL1.Low && f.Low < fL2.Low && f.Low < fR1.Low && f.Low < fR2.Low;

            decimal Confidence(bool isBuy, decimal breakDist, decimal barStrength, decimal trendBias)
            {
                decimal baseConf = 0.53m;

                // Break distance boost (scaled)
                var distBoost = Math.Clamp(breakDist * 50m, 0m, 0.03m);

                // Bar strength boost (closing near high/low)
                var barBoost = Math.Clamp((isBuy ? barStrength : (1 - barStrength)) * 0.05m, 0m, 0.02m);

                // Trend alignment boost
                var trendBoost = trendBias * 0.02m;

                return Math.Clamp(baseConf + distBoost + barBoost + trendBoost, baseConf, 0.60m);
            }

            // Measure short-term trend with last 5 closes
            decimal TrendBias()
            {
                var closes = hist.Skip(hist.Count - 5).Select(h => h.Close).ToList();
                return closes.Last() > closes.First() ? 1m : -1m;
            }

            var barRange = (last.High - last.Low) == 0 ? 1 : (last.High - last.Low);
            var barStrength = (last.Close - last.Low) / barRange;
            var trendBias = TrendBias();

            if (upFractal && last.Close > f.High)
            {
                var breakDist = (last.Close - f.High) / (f.High == 0 ? 1 : f.High);
                yield return new(sym, tf, Name, "BUY",
                    Confidence(true, breakDist, barStrength, trendBias > 0 ? 1m : 0m),
                    last.Close, "up-frac");
            }

            if (dnFractal && last.Close < f.Low)
            {
                var breakDist = (f.Low - last.Close) / (f.Low == 0 ? 1 : f.Low);
                yield return new(sym, tf, Name, "SELL",
                    Confidence(false, breakDist, barStrength, trendBias < 0 ? 1m : 0m),
                    last.Close, "dn-frac");
            }
        }
    }

    public sealed class ThreeSoldiersCrows : IPatternDetector
    {
        public string Name => "ThreeSoldiersCrows";

        public IEnumerable<Recommendation> Evaluate(string sym, TimeSpan tf, IReadOnlyList<TickerBar> hist)
        {
            if (hist.Count < 4) yield break;
            var a = hist[^3];
            var b = hist[^2];
            var c = hist[^1];

            bool up = a.Close > a.Open && b.Close > b.Open && c.Close > c.Open &&
                      b.Open <= a.Close && c.Open <= b.Close &&
                      a.Close > a.Open && b.Close > a.Close && c.Close > b.Close;

            bool dn = a.Close < a.Open && b.Close < b.Open && c.Close < c.Open &&
                      b.Open >= a.Close && c.Open >= b.Close &&
                      a.Close < a.Open && b.Close < a.Close && c.Close < b.Close;

            decimal Confidence(bool isBuy)
            {
                decimal baseConf = 0.55m;

                // Average body size across 3 bars
                decimal Body(TickerBar bar) => Math.Abs(bar.Close - bar.Open);
                var bodies = new[] { Body(a), Body(b), Body(c) };
                var avgBody = bodies.Average();
                var avgRange = new[] { a.High - a.Low, b.High - b.Low, c.High - c.Low }.Average();

                // Body-to-range ratio: stronger if bodies are full (closer to engulfing)
                var bodyStrength = avgRange == 0 ? 0m : avgBody / avgRange;
                var bodyBoost = Math.Clamp(bodyStrength * 0.05m, 0m, 0.03m);

                // Consistency: check similarity of body sizes (low variance = cleaner soldiers/crows)
                var varBodies = bodies.Select(x => (x - avgBody) * (x - avgBody)).Average();
                var stdBodies = (decimal)Math.Sqrt((double)varBodies);
                var consistency = avgBody == 0 ? 0m : 1m - (stdBodies / avgBody);
                var consistencyBoost = Math.Clamp(consistency * 0.03m, 0m, 0.02m);

                // Trend alignment: check last 5 closes
                var closes = hist.Skip(hist.Count - 5).Select(h => h.Close).ToList();
                bool trendingUp = closes.Last() > closes.First();
                bool trendingDn = closes.Last() < closes.First();
                var trendBoost = (isBuy && trendingUp) || (!isBuy && trendingDn) ? 0.02m : 0m;

                return Math.Clamp(baseConf + bodyBoost + consistencyBoost + trendBoost, baseConf, 0.62m);
            }

            if (up)
                yield return new(sym, tf, Name, "BUY", Confidence(true), c.Close, "3-soldiers");

            if (dn)
                yield return new(sym, tf, Name, "SELL", Confidence(false), c.Close, "3-crows");
        }
    }

    public sealed class MorningEveningStar : IPatternDetector
    {
        public string Name => "MorningEveningStar";

        public IEnumerable<Recommendation> Evaluate(string sym, TimeSpan tf, IReadOnlyList<TickerBar> hist)
        {
            if (hist.Count < 4) yield break;

            var a = hist[^3];
            var b = hist[^2];
            var c = hist[^1];

            bool aBear = a.Close < a.Open;
            bool aBull = a.Close > a.Open;
            bool cBull = c.Close > c.Open;
            bool cBear = c.Close < c.Open;

            decimal Body(TickerBar bar) => Math.Abs(bar.Close - bar.Open);
            decimal Range(TickerBar bar) => bar.High - bar.Low;

            var smallB = Body(b) / (Range(b) == 0 ? 1 : Range(b)) <= 0.3m;

            decimal Confidence(bool isBuy)
            {
                decimal baseConf = 0.53m;

                // 1. Body size alignment: big bodies on A and C, tiny B
                var bodyA = Body(a);
                var bodyB = Body(b);
                var bodyC = Body(c);
                var avgRange = (Range(a) + Range(b) + Range(c)) / 3m;
                var bodyStrength = avgRange == 0 ? 0m : (bodyA + bodyC - bodyB) / avgRange;
                var bodyBoost = Math.Clamp(bodyStrength * 0.03m, 0m, 0.03m);

                // 2. Confirmation strength: C closing far beyond midpoint of A
                var midA = (a.Open + a.Close) / 2m;
                decimal confStrength;
                if (isBuy)
                    confStrength = a.Open == a.Close ? 0m : (c.Close - midA) / Math.Abs(a.Open - a.Close);
                else
                    confStrength = a.Open == a.Close ? 0m : (midA - c.Close) / Math.Abs(a.Open - a.Close);
                var confBoost = Math.Clamp(confStrength * 0.03m, 0m, 0.03m);

                // 3. Trend context: look at closes of prior 3 bars
                var closes = hist.Skip(hist.Count - 5).Select(h => h.Close).ToList();
                bool trendingUp = closes.Last() > closes.First();
                bool trendingDn = closes.Last() < closes.First();
                var trendBoost = (isBuy && trendingDn) || (!isBuy && trendingUp) ? 0.02m : 0m;

                return Math.Clamp(baseConf + bodyBoost + confBoost + trendBoost, baseConf, 0.61m);
            }

            if (aBear && smallB && cBull && c.Close > (a.Open + a.Close) / 2m)
                yield return new(sym, tf, Name, "BUY", Confidence(true), c.Close, "morning-star");

            if (aBull && smallB && cBear && c.Close < (a.Open + a.Close) / 2m)
                yield return new(sym, tf, Name, "SELL", Confidence(false), c.Close, "evening-star");
        }
    }

    public sealed class TweezerTopBottom : IPatternDetector
    {
        public string Name => "Tweezer";

        public IEnumerable<Recommendation> Evaluate(string sym, TimeSpan tf, IReadOnlyList<TickerBar> hist)
        {
            if (hist.Count < 3) yield break;

            var a = hist[^2];
            var b = hist[^1];

            var avgRange = ((a.High - a.Low) + (b.High - b.Low)) / 2m;
            if (avgRange <= 0) yield break;

            var tol = avgRange * 0.05m; // 5% tolerance
            bool top = Math.Abs(a.High - b.High) <= tol && a.Close > a.Open && b.Close < b.Open;
            bool bot = Math.Abs(a.Low - b.Low) <= tol && a.Close < a.Open && b.Close > b.Open;

            decimal Confidence(bool isBuy)
            {
                decimal baseConf = 0.50m;

                // 1. Symmetry precision
                var diff = isBuy ? Math.Abs(a.Low - b.Low) : Math.Abs(a.High - b.High);
                var symBoost = Math.Clamp((1m - diff / (tol == 0 ? 1 : tol)) * 0.03m, 0m, 0.03m);

                // 2. Body strength (relative to avg range)
                decimal bodyA = Math.Abs(a.Close - a.Open);
                decimal bodyB = Math.Abs(b.Close - b.Open);
                var bodyBoost = Math.Clamp(((bodyA + bodyB) / avgRange) * 0.02m, 0m, 0.02m);

                // 3. Trend context: lookback 3 bars before pattern
                var closes = hist.Skip(hist.Count - 5).Take(3).Select(h => h.Close).ToList();
                bool trendingUp = closes.Last() > closes.First();
                bool trendingDn = closes.Last() < closes.First();
                var trendBoost = (isBuy && trendingDn) || (!isBuy && trendingUp) ? 0.02m : 0m;

                return Math.Clamp(baseConf + symBoost + bodyBoost + trendBoost, baseConf, 0.57m);
            }

            if (top)
                yield return new(sym, tf, Name, "SELL", Confidence(false), b.Close, "tweezer-top");

            if (bot)
                yield return new(sym, tf, Name, "BUY", Confidence(true), b.Close, "tweezer-bot");
        }
    }

    public sealed class BollingerReversion : IPatternDetector
    {
        public string Name => "BollReversion";
        private readonly int _period;
        private readonly decimal _mult;
        private readonly decimal _thresh;

        public BollingerReversion(int period = 20, decimal mult = 2m, decimal thresh = 0.002m)
        {
            _period = period;
            _mult = mult;
            _thresh = thresh;
        }

        public IEnumerable<Recommendation> Evaluate(string sym, TimeSpan tf, IReadOnlyList<TickerBar> hist)
        {
            if (hist.Count < _period + 1) yield break;

            var closes = hist.Select(h => h.Close).ToList();
            var slice = closes.Skip(closes.Count - _period).ToList();

            var mid = slice.Average();
            var std = TA.StdDev(slice);
            if (std <= 0) yield break;

            var upper = mid + _mult * std;
            var lower = mid - _mult * std;
            var last = hist[^1];

            var overU = (last.Close - upper) / (upper == 0 ? 1 : upper);
            var underL = (lower - last.Close) / (lower == 0 ? 1 : lower);

            decimal Confidence(decimal excursion, decimal bandWidth)
            {
                decimal baseConf = 0.51m;

                // excursion factor (0 → no signal, >thresh → boost)
                var excBoost = Math.Clamp((excursion - _thresh) * 8m, 0m, 0.05m);

                // band width factor (narrower bands mean excursion is more significant)
                var bwFactor = bandWidth / (mid == 0 ? 1 : mid);
                var bandBoost = bwFactor < 0.02m ? 0.02m : (bwFactor < 0.05m ? 0.01m : 0m);

                return Math.Clamp(baseConf + excBoost + bandBoost, baseConf, 0.60m);
            }

            var bandWidth = upper - lower;

            if (underL > _thresh)
                yield return new(sym, tf, Name, "BUY",
                    Confidence(underL, bandWidth), last.Close, "below-BB");

            if (overU > _thresh)
                yield return new(sym, tf, Name, "SELL",
                    Confidence(overU, bandWidth), last.Close, "above-BB");
        }
    }

    public sealed class AdxDiCross : IPatternDetector
    {
        public string Name => "ADX-DI";
        private readonly int _n;
        private readonly decimal _th;

        public AdxDiCross(int n = 14, decimal threshold = 20m)
        {
            _n = n;
            _th = threshold;
        }

        public IEnumerable<Recommendation> Evaluate(string sym, TimeSpan tf, IReadOnlyList<TickerBar> hist)
        {
            if (hist.Count < _n + 2) yield break;

            int start = hist.Count - _n - 1;
            decimal trSum = 0m, plusDmSum = 0m, minusDmSum = 0m;

            for (int i = start + 1; i < hist.Count; i++)
            {
                var hi = hist[i].High;
                var lo = hist[i].Low;
                var clPrev = hist[i - 1].Close;

                var tr = Math.Max((double)(hi - lo),
                            Math.Max((double)Math.Abs(hi - clPrev),
                                     (double)Math.Abs(lo - clPrev)));
                trSum += (decimal)tr;

                var upMove = hist[i].High - hist[i - 1].High;
                var dnMove = hist[i - 1].Low - hist[i].Low;

                if (upMove > 0 && upMove > dnMove) plusDmSum += upMove;
                if (dnMove > 0 && dnMove > upMove) minusDmSum += dnMove;
            }

            if (trSum == 0) yield break;

            var plusDI = 100m * plusDmSum / trSum;
            var minusDI = 100m * minusDmSum / trSum;
            var dx = 100m * Math.Abs(plusDI - minusDI) /
                     (plusDI + minusDI == 0 ? 1 : (plusDI + minusDI));

            var last = hist[^1];

            if (dx < _th) yield break; // only fire when ADX strong enough

            decimal Confidence(decimal adx, decimal diff)
            {
                decimal baseConf = 0.52m;

                // ADX boost: stronger trend = more confidence (cap at +0.06)
                var adxBoost = Math.Clamp((adx - _th) / 50m, 0m, 0.06m);

                // DI spread boost: how decisive is the winner? (cap at +0.04)
                var spreadBoost = Math.Clamp(diff / 50m, 0m, 0.04m);

                return Math.Clamp(baseConf + adxBoost + spreadBoost, baseConf, 0.62m);
            }

            if (plusDI > minusDI)
            {
                yield return new(sym, tf, Name, "BUY",
                    Confidence(dx, plusDI - minusDI),
                    last.Close, $"ADX={dx:F1}");
            }
            else if (minusDI > plusDI)
            {
                yield return new(sym, tf, Name, "SELL",
                    Confidence(dx, minusDI - plusDI),
                    last.Close, $"ADX={dx:F1}");
            }
        }
    }

    // =====================
    // TA helpers
    // =====================
    internal static class TA
    {
        public static decimal EMA(IReadOnlyList<decimal> xs, int period)
        {
            if (xs.Count == 0) return 0m;
            var k = 2m / (period + 1);
            decimal e = xs[0];
            for (int i = 1; i < xs.Count; i++) e = xs[i] * k + e * (1 - k);
            return e;
        }
        public static decimal ATR(IReadOnlyList<TickerBar> bars, int period)
        {
            if (bars.Count < period + 1) return 0m;
            decimal sum = 0m;
            for (int i = bars.Count - period; i < bars.Count; i++)
            {
                var t = bars[i]; var p = bars[i - 1];
                var tr = Math.Max((double)(t.High - t.Low), Math.Max((double)Math.Abs(t.High - p.Close), (double)Math.Abs(t.Low - p.Close)));
                sum += (decimal)tr;
            }
            return sum / period;
        }
        public static decimal StdDev(IReadOnlyList<decimal> xs)
        {
            if (xs.Count == 0) return 0m;
            var mean = xs.Average();
            var varv = xs.Select(v => (v - mean) * (v - mean)).Average();
            return (decimal)Math.Sqrt((double)varv);
        }
    }
}