using BacktesterStandalone.Engine;

namespace BacktesterApp.Engine
{
    public interface IPatternDetector
    {
        string Name { get; }
        IEnumerable<Recommendation> Evaluate(string sym, TimeSpan tf, IReadOnlyList<TickerBarBacktest> hist);
    }

    public class PatternEngine
    {
        private readonly List<IPatternDetector> _detectors = new();
        public PatternEngine Add(IPatternDetector d) { _detectors.Add(d); return this; }
        public IEnumerable<IPatternDetector> Detectors => _detectors;

        public IEnumerable<Recommendation> EvaluateAll(string sym, TimeSpan tf, IReadOnlyList<TickerBarBacktest> hist)
        {
            foreach (var d in _detectors)
            {
                IEnumerable<Recommendation> rs = Array.Empty<Recommendation>();
                try { rs = d.Evaluate(sym, tf, hist); } catch { /* swallow per-detector errors during backtest */ }
                foreach (var r in rs) yield return r;
            }
        }

        public static PatternEngine Default()
        {
            // NOTE: Keep this list in sync with your live Observer engine.
            // I add a handful as examples; paste/expand with your full set.
            return new PatternEngine()
                .Add(new Rsi50())
                .Add(new RsiAdaptive())
                .Add(new RsiExtreme())
                .Add(new StochasticCross())
                .Add(new InsideBarBreak())
                .Add(new InsideBarWickBreak())
                .Add(new InsideBarNR7Break())
                .Add(new InsideBarTrendBreak())
                .Add(new MacdCross())
                .Add(new AtrBreakout())
                .Add(new DonchianBreakout())
                .Add(new PullbackToEma())
                .Add(new Engulfing())
                .Add(new AdxDiCross())
                .Add(new VwapMultiWindowAlign())
                .Add(new Nr7Breakout())
                .Add(new BollingerReversion())
                ;
        }
    }
}