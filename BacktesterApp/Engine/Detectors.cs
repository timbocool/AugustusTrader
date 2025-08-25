using BacktesterStandalone.Engine;
using System.Collections.Generic;
using System.Linq;

namespace BacktesterApp.Engine
{
    // --- helpers ---
    static class TA
    {
        public static decimal EMA(IList<decimal> xs, int n)
        {
            if (xs.Count == 0) return 0m;
            decimal k = 2m / (n + 1);
            decimal ema = xs[0];
            for (int i = 0; i < xs.Count; i++)
            {
                var x = xs[i];
                ema = i == 0 ? x : x * k + ema * (1 - k);
            }
            return ema;
        }
        public static decimal ATR(IReadOnlyList<TickerBarBacktest> hist, int n)
        {
            if (hist.Count < n + 1) return 0m;
            var trs = new List<decimal>();
            for (int i = hist.Count - n; i < hist.Count; i++)
            {
                var hi = hist[i].High; var lo = hist[i].Low; var cp = hist[i - 1].Close;
                var tr = (decimal)Math.Max((double)(hi - lo), Math.Max((double)Math.Abs(hi - cp), (double)Math.Abs(lo - cp)));
                trs.Add(tr);
            }
            return trs.Average();
        }
        public static decimal StdDev(IList<decimal> xs)
        {
            if (xs.Count == 0) return 0m;
            var mean = xs.Average();
            var var_ = xs.Select(x => (x - mean) * (x - mean)).Average();
            return (decimal)Math.Sqrt((double)var_);
        }
    }

    // --- detectors (subset; paste the rest from your live engine as needed) ---

    public sealed class Rsi50 : IPatternDetector
    {
        public string Name => "Rsi50";
        private readonly int _n;
        public Rsi50(int n=14) { _n = n; }
        public IEnumerable<Recommendation> Evaluate(string sym, TimeSpan tf, IReadOnlyList<TickerBarBacktest> hist)
        {
            if (hist.Count < _n + 2) yield break;
            var closes = hist.Select(h => h.Close).ToList();
            // RSI
            decimal gain = 0, loss = 0;
            for (int i = closes.Count - _n; i < closes.Count; i++)
            {
                var d = closes[i] - closes[i - 1];
                if (d > 0) gain += d; else loss -= d;
            }
            if (gain + loss == 0) yield break;
            var rs = gain / (loss == 0 ? 1 : loss);
            var rsi = 100m - 100m / (1m + rs);
            var last = hist[^1];
            if (rsi > 50m) yield return new(sym, tf, Name, "BUY", 0.60m, last.Close, "rsi>50");
            else yield return new(sym, tf, Name, "SELL", 0.60m, last.Close, "rsi<50");
        }
    }

    public sealed class RsiAdaptive : IPatternDetector
    {
        public string Name => "RsiAdaptive";
        private readonly int _n;
        public RsiAdaptive(int n=14) { _n = n; }
        public IEnumerable<Recommendation> Evaluate(string sym, TimeSpan tf, IReadOnlyList<TickerBarBacktest> hist)
        {
            if (hist.Count < _n + 2) yield break;
            var closes = hist.Select(h => h.Close).ToList();
            decimal gain = 0, loss = 0;
            for (int i = closes.Count - _n; i < closes.Count; i++)
            {
                var d = closes[i] - closes[i - 1];
                if (d > 0) gain += d; else loss -= d;
            }
            if (gain + loss == 0) yield break;
            var rs = gain / (loss == 0 ? 1 : loss);
            var rsi = 100m - 100m / (1m + rs);
            var last = hist[^1];
            var pctl = 0.5m; // placeholder adaptive logic
            if (rsi > 50m + 10m * pctl) yield return new(sym, tf, Name, "SELL", 0.66m, last.Close, $"rsi=p80% ({rsi:F1})");
            if (rsi < 50m - 10m * pctl) yield return new(sym, tf, Name, "BUY", 0.66m, last.Close, $"rsi=p20% ({rsi:F1})");
        }
    }

    public sealed class RsiExtreme : IPatternDetector
    {
        public string Name => "RsiExtreme";
        private readonly int _n;
        public RsiExtreme(int n=14) { _n = n; }
        public IEnumerable<Recommendation> Evaluate(string sym, TimeSpan tf, IReadOnlyList<TickerBarBacktest> hist)
        {
            if (hist.Count < _n + 2) yield break;
            var closes = hist.Select(h => h.Close).ToList();
            decimal gain = 0, loss = 0;
            for (int i = closes.Count - _n; i < closes.Count; i++)
            {
                var d = closes[i] - closes[i - 1];
                if (d > 0) gain += d; else loss -= d;
            }
            if (gain + loss == 0) yield break;
            var rs = gain / (loss == 0 ? 1 : loss);
            var rsi = 100m - 100m / (1m + rs);
            var last = hist[^1];
            if (rsi >= 80m) yield return new(sym, tf, Name, "SELL", 0.70m, last.Close, $"rsi={rsi:F1}");
            if (rsi <= 20m) yield return new(sym, tf, Name, "BUY", 0.70m, last.Close, $"rsi={rsi:F1}");
        }
    }

    public sealed class StochasticCross : IPatternDetector
    {
        public string Name => "StochasticCross";
        private readonly int _kPeriod, _dPeriod; private readonly decimal _os, _ob;
        public StochasticCross(int kPeriod = 14, int dPeriod = 3, decimal oversold = 20m, decimal overbought = 80m)
        { _kPeriod = kPeriod; _dPeriod = dPeriod; _os = oversold; _ob = overbought; }
        public IEnumerable<Recommendation> Evaluate(string sym, TimeSpan tf, IReadOnlyList<TickerBarBacktest> hist)
        {
            if (hist.Count < _kPeriod + _dPeriod + 2) yield break;
            decimal K(int idx)
            {
                int start = Math.Max(0, idx - _kPeriod + 1);
                var slice = hist.Skip(start).Take(idx - start + 1).ToList();
                var hh = slice.Max(b => b.High);
                var ll = slice.Min(b => b.Low);
                var close = hist[idx].Close;
                var denom = hh - ll; if (denom == 0) return 50m;
                return 100m * (close - ll) / denom;
            }
            var kPrev = K(hist.Count - 2);
            var kNow = K(hist.Count - 1);
            decimal D(int idx)
            {
                var ks = new List<decimal>();
                for (int i = idx - _dPeriod + 1; i <= idx; i++) ks.Add(K(i));
                return ks.Average();
            }
            var dPrev = D(hist.Count - 2);
            var dNow = D(hist.Count - 1);
            var last = hist[^1];
            if (kPrev <= dPrev && kNow > dNow && kNow < _ob)
                yield return new(sym, tf, Name, "BUY", 0.51m, last.Close, $"Kx>D ({kNow:F1})");
            if (kPrev >= dPrev && kNow < dNow && kNow > _os)
                yield return new(sym, tf, Name, "SELL", 0.51m, last.Close, $"Kx<D ({kNow:F1})");
            if (kNow <= _os && kPrev < _os && kNow > dNow)
                yield return new(sym, tf, Name, "BUY", 0.52m, last.Close, $"K<{_os}");
            if (kNow >= _ob && kPrev > _ob && kNow < dNow)
                yield return new(sym, tf, Name, "SELL", 0.52m, last.Close, $"K>{_ob}");
        }
    }

    public sealed class InsideBarBreak : IPatternDetector
    {
        public string Name => "InsideBarBreak";
        public IEnumerable<Recommendation> Evaluate(string sym, TimeSpan tf, IReadOnlyList<TickerBarBacktest> hist)
        {
            if (hist.Count < 3) yield break;
            var a = hist[^2]; var b = hist[^1];
            bool inside = b.High <= a.High && b.Low >= a.Low;
            if (!inside) yield break;
            if (b.Close > a.High) yield return new(sym, tf, Name, "BUY", 0.55m, b.Close, "inside->up");
            if (b.Close < a.Low)  yield return new(sym, tf, Name, "SELL", 0.55m, b.Close, "inside->down");
        }
    }

    public sealed class InsideBarWickBreak : IPatternDetector
    {
        public string Name => "InsideBarWickBreak";
        public IEnumerable<Recommendation> Evaluate(string sym, TimeSpan tf, IReadOnlyList<TickerBarBacktest> hist)
        {
            if (hist.Count < 3) yield break;
            var a = hist[^2]; var b = hist[^1];
            bool inside = b.High <= a.High && b.Low >= a.Low;
            if (!inside) yield break;
            // wick-based confirmation: close within last 25% toward breakout
            var rng = a.High - a.Low; if (rng <= 0) yield break;
            var pos = (b.Close - a.Low) / rng;
            if (pos >= 0.75m) yield return new(sym, tf, Name, "BUY", 0.53m, b.Close, "inside->up(wick)");
            if (pos <= 0.25m) yield return new(sym, tf, Name, "SELL", 0.53m, b.Close, "inside->down(wick)");
        }
    }

    public sealed class InsideBarNR7Break : IPatternDetector
    {
        public string Name => "InsideBarNR7Break";
        public IEnumerable<Recommendation> Evaluate(string sym, TimeSpan tf, IReadOnlyList<TickerBarBacktest> hist)
        {
            if (hist.Count < 8) yield break;
            var prev7 = hist.Skip(hist.Count - 8).Take(7).ToList();
            var ranges = prev7.Select(b => b.High - b.Low).ToList();
            var minIdx = 0; decimal minR = ranges[0];
            for (int i = 1; i < ranges.Count; i++) if (ranges[i] < minR) { minR = ranges[i]; minIdx = i; }
            if (minIdx != prev7.Count - 1) yield break;
            var narrow = hist[^2]; var last = hist[^1];
            if (last.Close > narrow.High) yield return new(sym, tf, Name, "BUY", 0.60m, last.Close, "inside+NR7->up");
            if (last.Close < narrow.Low)  yield return new(sym, tf, Name, "SELL", 0.60m, last.Close, "inside+NR7->down");
        }
    }

    public sealed class InsideBarTrendBreak : IPatternDetector
    {
        public string Name => "InsideBarTrendBreak";
        public IEnumerable<Recommendation> Evaluate(string sym, TimeSpan tf, IReadOnlyList<TickerBarBacktest> hist)
        {
            if (hist.Count < 22) yield break;
            var closes = hist.Select(h => h.Close).ToList();
            var ema20 = TA.EMA(closes, 20);
            var last = hist[^1]; var a = hist[^2];
            bool inside = last.High <= a.High && last.Low >= a.Low;
            if (!inside) yield break;
            if (last.Close > ema20) yield return new(sym, tf, Name, "BUY", 0.62m, last.Close, "inside->up ema20");
            if (last.Close < ema20) yield return new(sym, tf, Name, "SELL", 0.62m, last.Close, "inside->down ema20");
        }
    }

    public sealed class MacdCross : IPatternDetector
    {
        public string Name => "MacdCross";
        private readonly int _fast, _slow, _signal;
        public MacdCross(int fast=12, int slow=26, int signal=9) { _fast=fast; _slow=slow; _signal=signal; }
        public IEnumerable<Recommendation> Evaluate(string sym, TimeSpan tf, IReadOnlyList<TickerBarBacktest> hist)
        {
            int need = Math.Max(_slow + _signal + 2, 40);
            if (hist.Count < need) yield break;
            var closes = hist.Select(h => h.Close).ToList();
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
            var last = hist[^1];
            if (macdPrev <= 0 && macdNow > 0) yield return new(sym, tf, Name, "BUY", 0.53m, last.Close, "macd>sig");
            if (macdPrev >= 0 && macdNow < 0) yield return new(sym, tf, Name, "SELL", 0.53m, last.Close, "macd<sig");
        }
    }

    public sealed class AtrBreakout : IPatternDetector
    {
        public string Name => "AtrBreakout";
        private readonly int _n; private readonly decimal _k;
        public AtrBreakout(int n=14, decimal k=1.5m) { _n=n; _k=k; }
        public IEnumerable<Recommendation> Evaluate(string sym, TimeSpan tf, IReadOnlyList<TickerBarBacktest> hist)
        {
            if (hist.Count < _n + 2) yield break;
            var atr = TA.ATR(hist, _n);
            if (atr <= 0) yield break;
            var last = hist[^1]; var prev = hist[^2];
            if (last.Close >= prev.High + _k * atr)
                yield return new(sym, tf, Name, "BUY", 0.55m, last.Close, $"atr={atr:F4}");
            if (last.Close <= prev.Low - _k * atr)
                yield return new(sym, tf, Name, "SELL", 0.55m, last.Close, $"atr={atr:F4}");
        }
    }

    public sealed class DonchianBreakout : IPatternDetector
    {
        public string Name => "DonchianBreakout";
        private readonly int _lookback;
        private readonly decimal _bufferPct;
        public DonchianBreakout(int lookback = 20, decimal bufferPct = 0.0m)
        { _lookback = lookback; _bufferPct = bufferPct; }
        public IEnumerable<Recommendation> Evaluate(string sym, TimeSpan tf, IReadOnlyList<TickerBarBacktest> hist)
        {
            if (hist.Count < _lookback + 2) yield break;
            var window = hist.Skip(hist.Count - _lookback - 1).Take(_lookback).ToList();
            var hh = window.Max(b => b.High);
            var ll = window.Min(b => b.Low);
            var last = hist[^1];
            var up = hh * (1 + _bufferPct);
            var dn = ll * (1 - _bufferPct);
            if (last.Close >= up) yield return new(sym, tf, Name, "BUY", 0.56m, last.Close, $">{_lookback}H");
            if (last.Close <= dn) yield return new(sym, tf, Name, "SELL", 0.56m, last.Close, $"<{_lookback}L");
        }
    }

    public sealed class PullbackToEma : IPatternDetector
    {
        public string Name => "PullbackToEMA";
        private readonly int _period; private readonly decimal _touchTol;
        public PullbackToEma(int period = 20, decimal touchTol = 0.001m) { _period = period; _touchTol = touchTol; }
        public IEnumerable<Recommendation> Evaluate(string sym, TimeSpan tf, IReadOnlyList<TickerBarBacktest> hist)
        {
            if (hist.Count < _period + 3) yield break;
            var closes = hist.Select(h => h.Close).ToList();
            var emaNow = TA.EMA(closes, _period);
            var emaPrev = TA.EMA(closes.Take(closes.Count - 1).ToList(), _period);
            var last = hist[^1]; var prev = hist[^2];
            var upTrend = last.Close > emaNow && emaNow > emaPrev;
            var dnTrend = last.Close < emaNow && emaNow < emaPrev;
            bool touched = Math.Abs((last.Low + last.High) / 2m - emaNow) / (emaNow == 0 ? 1 : emaNow) <= _touchTol || last.Low <= emaNow && last.High >= emaNow;
            if (upTrend && touched && last.Close > prev.High)
                yield return new(sym, tf, Name, "BUY", 0.54m, last.Close, "pbk-ema");
            if (dnTrend && touched && last.Close < prev.Low)
                yield return new(sym, tf, Name, "SELL", 0.54m, last.Close, "pbk-ema");
        }
    }

    public sealed class Engulfing : IPatternDetector
    {
        public string Name => "Engulfing";
        public IEnumerable<Recommendation> Evaluate(string sym, TimeSpan tf, IReadOnlyList<TickerBarBacktest> hist)
        {
            if (hist.Count < 2) yield break;
            var a = hist[^2]; var b = hist[^1];
            var aBull = a.Close > a.Open; var aBear = a.Close < a.Open;
            var bBull = b.Close > b.Open; var bBear = b.Close < b.Open;
            var aBody = Math.Abs(a.Close - a.Open);
            var bBody = Math.Abs(b.Close - b.Open);
            var minBody = (a.High - a.Low) * 0.15m; // avoid tiny bodies
            if (bBull && aBear && b.Open <= a.Close && b.Close >= a.Open && bBody > minBody)
                yield return new(sym, tf, Name, "BUY", 0.52m, b.Close, "bull-engulf");
            if (bBear && aBull && b.Open >= a.Close && b.Close <= a.Open && bBody > minBody)
                yield return new(sym, tf, Name, "SELL", 0.52m, b.Close, "bear-engulf");
        }
    }

    public sealed class AdxDiCross : IPatternDetector
    {
        public string Name => "ADX-DI";
        private readonly int _n; private readonly decimal _th;
        public AdxDiCross(int n = 14, decimal threshold = 20m) { _n = n; _th = threshold; }
        public IEnumerable<Recommendation> Evaluate(string sym, TimeSpan tf, IReadOnlyList<TickerBarBacktest> hist)
        {
            if (hist.Count < _n + 2) yield break;
            int start = hist.Count - _n - 1;
            decimal trSum = 0m, plusDmSum = 0m, minusDmSum = 0m;
            for (int i = start + 1; i < hist.Count; i++)
            {
                var hi = hist[i].High; var lo = hist[i].Low; var clPrev = hist[i - 1].Close;
                var tr = Math.Max((double)(hi - lo), Math.Max((double)Math.Abs(hi - clPrev), (double)Math.Abs(lo - clPrev)));
                trSum += (decimal)tr;
                var upMove = hist[i].High - hist[i - 1].High;
                var dnMove = hist[i - 1].Low - hist[i].Low;
                plusDmSum += upMove > 0 && upMove > dnMove ? upMove : 0m;
                minusDmSum += dnMove > 0 && dnMove > upMove ? dnMove : 0m;
            }
            if (trSum == 0) yield break;
            var plusDI = 100m * plusDmSum / trSum;
            var minusDI = 100m * minusDmSum / trSum;
            var dx = 100m * Math.Abs(plusDI - minusDI) / (plusDI + minusDI == 0 ? 1 : plusDI + minusDI);
            var last = hist[^1];
            if (dx >= _th)
            {
                if (plusDI > minusDI)
                    yield return new(sym, tf, Name, "BUY", 0.52m, last.Close, $"ADX={dx:F1}");
                else if (minusDI > plusDI)
                    yield return new(sym, tf, Name, "SELL", 0.52m, last.Close, $"ADX={dx:F1}");
            }
        }
    }

    public sealed class VwapMultiWindowAlign : IPatternDetector
    {
        public string Name => "VwapMultiVWAP";
        private readonly int _shortBars, _midBars;
        public VwapMultiWindowAlign(int shortBars = 20, int midBars = 60)
        { _shortBars = shortBars; _midBars = midBars; }
        public IEnumerable<Recommendation> Evaluate(string sym, TimeSpan tf, IReadOnlyList<TickerBarBacktest> hist)
        {
            if (hist.Count < Math.Max(_shortBars, _midBars)) yield break;

            decimal VwapBars(int n)
            {
                int start = hist.Count - n;
                decimal pv = 0m, v = 0m;
                for (int i = start; i < hist.Count; i++)
                {
                    var tp = (hist[i].High + hist[i].Low + hist[i].Close) / 3m;
                    pv += tp * hist[i].Volume;
                    v += hist[i].Volume;
                }
                return v == 0 ? 0 : pv / v;
            }

            var last = hist[^1];
            var vS = VwapBars(_shortBars);
            var vM = VwapBars(_midBars);
            if (vS <= 0 || vM <= 0) yield break;

            if (last.Close > vS && last.Close > vM)
                yield return new(sym, tf, Name, "BUY", 0.55m, last.Close, $"vwapS={vS:F4},vwapM={vM:F4}");
            if (last.Close < vS && last.Close < vM)
                yield return new(sym, tf, Name, "SELL", 0.55m, last.Close, $"vwapS={vS:F4},vwapM={vM:F4}");
        }
    }

    public sealed class Nr7Breakout : IPatternDetector
    {
        public string Name => "NR7Breakout";
        public IEnumerable<Recommendation> Evaluate(string sym, TimeSpan tf, IReadOnlyList<TickerBarBacktest> hist)
        {
            if (hist.Count < 8) yield break;
            var prev7 = hist.Skip(hist.Count - 8).Take(7).ToList();
            var ranges = prev7.Select(b => b.High - b.Low).ToList();
            var minIdx = 0; decimal minR = ranges[0];
            for (int i = 1; i < ranges.Count; i++) if (ranges[i] < minR) { minR = ranges[i]; minIdx = i; }
            if (minIdx != prev7.Count - 1) yield break;
            var narrow = hist[^2]; var last = hist[^1];
            if (last.Close > narrow.High) yield return new(sym, tf, Name, "BUY", 0.54m, last.Close, "breakNR7H");
            if (last.Close < narrow.Low)  yield return new(sym, tf, Name, "SELL", 0.54m, last.Close, "breakNR7L");
        }
    }

    public sealed class BollingerReversion : IPatternDetector
    {
        public string Name => "BollReversion";
        private readonly int _period; private readonly decimal _mult; private readonly decimal _thresh;
        public BollingerReversion(int period = 20, decimal mult = 2m, decimal thresh = 0.002m)
        { _period = period; _mult = mult; _thresh = thresh; }
        public IEnumerable<Recommendation> Evaluate(string sym, TimeSpan tf, IReadOnlyList<TickerBarBacktest> hist)
        {
            if (hist.Count < _period + 1) yield break;
            var closes = hist.Select(h => h.Close).ToList();
            var slice = closes.Skip(closes.Count - _period).ToList();
            var mid = slice.Average();
            var std = TA.StdDev(slice);
            var upper = mid + _mult * std;
            var lower = mid - _mult * std;
            var last = hist[^1];
            var overU = (last.Close - upper) / (upper == 0 ? 1 : upper);
            var underL = (lower - last.Close) / (lower == 0 ? 1 : lower);
            if (underL > _thresh)
                yield return new(sym, tf, Name, "BUY", 0.51m, last.Close, "below-BB");
            if (overU > _thresh)
                yield return new(sym, tf, Name, "SELL", 0.51m, last.Close, "above-BB");
        }
    }
}