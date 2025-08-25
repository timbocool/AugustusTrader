using Serilog;
using Shared.Models;
using System.Collections.Concurrent;

namespace ObserverBot.Services
{
    public sealed class MtfAggregatorService
    {
        public event Action<TimeSpan, TickerBar>? OnTfBarClosed;
        public event Action<TimeSpan, string, IReadOnlyList<TickerBar>>? OnTfHistory;

        private readonly TimeSpan[] _tfs;
        private readonly ILogger _log;
        private readonly MetricsService _metrics;

        private readonly ConcurrentDictionary<TimeSpan, ConcurrentDictionary<string, TfState>> _states = new();

        public MtfAggregatorService(TimeSpan[] timeframes, ILogger log, MetricsService metrics)
        {
            _tfs = timeframes;
            _log = log;
            _metrics = metrics;
            foreach (var tf in _tfs)
                _states[tf] = new(StringComparer.OrdinalIgnoreCase);
        }

        public void OnOneMinuteBar(TickerBar m1)
        {
            foreach (var tf in _tfs)
            {
                var map = _states[tf];
                var st = map.GetOrAdd(m1.Symbol, _ => new TfState(tf, m1.Symbol));

                if (st.Ingest(m1, out var closed))
                {
                    _metrics.IncTfBar($"{m1.Symbol}|{FormatTf(tf)}");
                    OnTfBarClosed?.Invoke(tf, closed);
                    var hist = st.GetHistorySnapshot();
                    OnTfHistory?.Invoke(tf, m1.Symbol, hist);
                }
            }
        }

        /// <summary>
        /// Seed historical bars for a symbol/timeframe into the state,
        /// and replay them through events so the rest of the pipeline sees them.
        /// </summary>
        public void PrimeHistory(TimeSpan tf, string symbol, IEnumerable<TickerBar> history, int historyLimit = 1500)
        {
            if (!_states.TryGetValue(tf, out var map))
                return;

            var st = map.GetOrAdd(symbol, _ => new TfState(tf, symbol, historyLimit));

            foreach (var bar in history.OrderBy(b => b.MinuteUtc))
            {
                st.SeedBar(bar);
                _metrics.IncTfBar($"{bar.Symbol}|{FormatTf(tf)}");
                OnTfBarClosed?.Invoke(tf, bar);
            }

            // After seeding, push the full snapshot
            OnTfHistory?.Invoke(tf, symbol, st.GetHistorySnapshot());
        }

        private static string FormatTf(TimeSpan tf)
        {
            if (tf.TotalDays >= 1) return $"{tf.TotalDays:0.#}d";
            if (tf.TotalHours >= 1) return $"{tf.TotalHours:0.#}h";
            return $"{tf.TotalMinutes:0.#}m";
        }

        private sealed class TfState
        {
            private readonly TimeSpan _tf;
            private readonly string _symbol;
            private DateTime _startUtc;
            private bool _hasOpen;
            private decimal _o, _h, _l, _c, _v;
            private readonly int _cap;
            private readonly List<TickerBar> _history;

            public TfState(TimeSpan tf, string symbol, int cap = 1500)
            {
                _tf = tf;
                _symbol = symbol;
                _cap = cap;
                _history = new(cap);
            }

            public bool Ingest(TickerBar m1, out TickerBar? closed)
            {
                closed = null;
                var bucketStart = Align(m1.MinuteUtc, _tf);

                if (!_hasOpen)
                {
                    Start(bucketStart, m1);
                    return false;
                }

                if (bucketStart != _startUtc)
                {
                    closed = ToBar();
                    Start(bucketStart, m1);
                    AppendHistory(closed);
                    return true;
                }

                _h = Math.Max(_h, m1.High);
                _l = Math.Min(_l, m1.Low);
                _c = m1.Close;
                _v += (decimal)m1.Volume;
                return false;
            }

            public IReadOnlyList<TickerBar> GetHistorySnapshot() => _history;

            /// <summary>
            /// Append a single bar into history (used during seeding).
            /// </summary>
            public void SeedBar(TickerBar bar) => AppendHistory(bar);

            private void Start(DateTime start, TickerBar m1)
            {
                _startUtc = start;
                _o = m1.Open;
                _h = Math.Max(m1.Open, Math.Max(m1.High, m1.Close));
                _l = Math.Min(m1.Open, Math.Min(m1.Low, m1.Close));
                _c = m1.Close;
                _v = (decimal)m1.Volume;
                _hasOpen = true;
            }

            private TickerBar ToBar() => new()
            {
                Symbol = _symbol,
                MinuteUtc = _startUtc,
                Open = _o,
                High = _h,
                Low = _l,
                Close = _c,
                Volume = _v,
                Vwap60s = 0
            };

            private void AppendHistory(TickerBar b)
            {
                _history.Add(b);
                if (_history.Count > _cap)
                    _history.RemoveRange(0, _history.Count - _cap);
            }

            private static DateTime Align(DateTime t, TimeSpan tf)
            {
                if (tf >= TimeSpan.FromDays(1))
                {
                    var days = (int)tf.TotalDays;
                    var baseDay = new DateTime(t.Year, t.Month, t.Day, 0, 0, 0, DateTimeKind.Utc);
                    var epochDays = (baseDay - DateTime.UnixEpoch).TotalDays;
                    var bucket = Math.Floor(epochDays / days) * days;
                    return DateTime.UnixEpoch.AddDays(bucket);
                }
                else
                {
                    var ticks = tf.Ticks;
                    var sinceEpoch = (t - DateTime.UnixEpoch).Ticks;
                    var floored = (sinceEpoch / ticks) * ticks;
                    return DateTime.UnixEpoch.AddTicks(floored);
                }
            }
        }
    }
}



////using Serilog;
////using System;
////using System.Collections.Concurrent;
////using System.Collections.Generic;
////using ObserverBot.Models;

////namespace ObserverBot.Services
////{
////    public sealed class MtfAggregatorService
////    {
////        public event Action<TimeSpan, TickerBar>? OnTfBarClosed;
////        public event Action<TimeSpan, string, IReadOnlyList<TickerBar>>? OnTfHistory;

////        private readonly TimeSpan[] _tfs;
////        private readonly ILogger _log;
////        private readonly MetricsService _metrics;

////        private readonly ConcurrentDictionary<TimeSpan, ConcurrentDictionary<string, TfState>> _states = new();

////        public MtfAggregatorService(TimeSpan[] timeframes, ILogger log, MetricsService metrics)
////        {
////            _tfs = timeframes;
////            _log = log;
////            _metrics = metrics;
////            foreach (var tf in _tfs)
////                _states[tf] = new(StringComparer.OrdinalIgnoreCase);
////        }

////        public void OnOneMinuteBar(TickerBar m1)
////        {
////            foreach (var tf in _tfs)
////            {
////                var map = _states[tf];
////                var st = map.GetOrAdd(m1.Symbol, _ => new TfState(tf, m1.Symbol));

////                if (st.Ingest(m1, out var closed))
////                {
////                    _metrics.IncTfBar($"{m1.Symbol}|{FormatTf(tf)}");
////                    OnTfBarClosed?.Invoke(tf, closed);
////                    var hist = st.GetHistorySnapshot();
////                    OnTfHistory?.Invoke(tf, m1.Symbol, hist);
////                }
////            }
////        }

////        private static string FormatTf(TimeSpan tf)
////        {
////            if (tf.TotalDays >= 1)  return $"{tf.TotalDays:0.#}d";
////            if (tf.TotalHours >= 1) return $"{tf.TotalHours:0.#}h";
////            return $"{tf.TotalMinutes:0.#}m";
////        }

////        private sealed class TfState
////        {
////            private readonly TimeSpan _tf;
////            private readonly string _symbol;
////            private DateTime _startUtc;
////            private bool _hasOpen;
////            private decimal _o, _h, _l, _c, _v;
////            private readonly int _cap;
////            private readonly List<TickerBar> _history;

////            public TfState(TimeSpan tf, string symbol, int cap = 1500)
////            {
////                _tf = tf;
////                _symbol = symbol;
////                _cap = cap;
////                _history = new(cap);
////            }

////            public bool Ingest(TickerBar m1, out TickerBar? closed)
////            {
////                closed = null;
////                var bucketStart = Align(m1.MinuteUtc, _tf);

////                if (!_hasOpen)
////                {
////                    Start(bucketStart, m1);
////                    return false;
////                }

////                if (bucketStart != _startUtc)
////                {
////                    closed = ToBar();
////                    Start(bucketStart, m1);
////                    AppendHistory(closed);
////                    return true;
////                }

////                _h = Math.Max(_h, m1.High);
////                _l = Math.Min(_l, m1.Low);
////                _c = m1.Close;
////                _v += m1.Volume;
////                return false;
////            }

////            public IReadOnlyList<TickerBar> GetHistorySnapshot() => _history;

////            private void Start(DateTime start, TickerBar m1)
////            {
////                _startUtc = start;
////                _o = m1.Open;
////                _h = Math.Max(m1.Open, Math.Max(m1.High, m1.Close));
////                _l = Math.Min(m1.Open, Math.Min(m1.Low,  m1.Close));
////                _c = m1.Close;
////                _v = m1.Volume;
////                _hasOpen = true;
////            }

////            private TickerBar ToBar() => new()
////            {
////                Symbol = _symbol,
////                MinuteUtc = _startUtc,
////                Open = _o, High = _h, Low = _l, Close = _c,
////                Volume = _v,
////                Vwap60s = 0
////            };

////            private void AppendHistory(TickerBar b)
////            {
////                _history.Add(b);
////                if (_history.Count > _cap)
////                    _history.RemoveRange(0, _history.Count - _cap);
////            }

////            private static DateTime Align(DateTime t, TimeSpan tf)
////            {
////                if (tf >= TimeSpan.FromDays(1))
////                {
////                    var days = (int)tf.TotalDays;
////                    var baseDay = new DateTime(t.Year, t.Month, t.Day, 0, 0, 0, DateTimeKind.Utc);
////                    var epochDays = (baseDay - DateTime.UnixEpoch).TotalDays;
////                    var bucket = Math.Floor(epochDays / days) * days;
////                    return DateTime.UnixEpoch.AddDays(bucket);
////                }
////                else
////                {
////                    var ticks = tf.Ticks;
////                    var sinceEpoch = (t - DateTime.UnixEpoch).Ticks;
////                    var floored = (sinceEpoch / ticks) * ticks;
////                    return DateTime.UnixEpoch.AddTicks(floored);
////                }
////            }



////        }
////    }
////}

//using Serilog;
//using System;
//using System.Collections.Concurrent;
//using System.Collections.Generic;
//using System.Linq;
//using ObserverBot.Models;

//namespace ObserverBot.Services
//{
//    public sealed class MtfAggregatorService
//    {
//        public event Action<TimeSpan, TickerBar>? OnTfBarClosed;
//        public event Action<TimeSpan, string, IReadOnlyList<TickerBar>>? OnTfHistory;

//        private readonly TimeSpan[] _tfs;
//        private readonly ILogger _log;
//        private readonly MetricsService _metrics;

//        private readonly ConcurrentDictionary<TimeSpan, ConcurrentDictionary<string, TfState>> _states = new();

//        public MtfAggregatorService(TimeSpan[] timeframes, ILogger log, MetricsService metrics)
//        {
//            _tfs = timeframes;
//            _log = log;
//            _metrics = metrics;
//            foreach (var tf in _tfs)
//                _states[tf] = new(StringComparer.OrdinalIgnoreCase);
//        }

//        public void OnOneMinuteBar(TickerBar m1)
//        {
//            foreach (var tf in _tfs)
//            {
//                var map = _states[tf];
//                var st = map.GetOrAdd(m1.Symbol, _ => new TfState(tf, m1.Symbol));

//                if (st.Ingest(m1, out var closed))
//                {
//                    _metrics.IncTfBar($"{m1.Symbol}|{FormatTf(tf)}");
//                    OnTfBarClosed?.Invoke(tf, closed);
//                    var hist = st.GetHistorySnapshot();
//                    OnTfHistory?.Invoke(tf, m1.Symbol, hist);
//                }
//            }
//        }

//        /// <summary>
//        /// Seed historical bars for a symbol and timeframe into the state.
//        /// This allows backfilled candles from DB/REST to be merged into the rolling window.
//        /// </summary>
//        public void PrimeHistory(TimeSpan tf, string symbol, IEnumerable<TickerBar> history, int historyLimit = 1500)
//        {
//            if (!_states.TryGetValue(tf, out var map))
//                return;

//            var st = map.GetOrAdd(symbol, _ => new TfState(tf, symbol, historyLimit));

//            st.SeedHistory(history);
//        }

//        private static string FormatTf(TimeSpan tf)
//        {
//            if (tf.TotalDays >= 1) return $"{tf.TotalDays:0.#}d";
//            if (tf.TotalHours >= 1) return $"{tf.TotalHours:0.#}h";
//            return $"{tf.TotalMinutes:0.#}m";
//        }

//        private sealed class TfState
//        {
//            private readonly TimeSpan _tf;
//            private readonly string _symbol;
//            private DateTime _startUtc;
//            private bool _hasOpen;
//            private decimal _o, _h, _l, _c, _v;
//            private readonly int _cap;
//            private readonly List<TickerBar> _history;

//            public TfState(TimeSpan tf, string symbol, int cap = 1500)
//            {
//                _tf = tf;
//                _symbol = symbol;
//                _cap = cap;
//                _history = new(cap);
//            }

//            public bool Ingest(TickerBar m1, out TickerBar? closed)
//            {
//                closed = null;
//                var bucketStart = Align(m1.MinuteUtc, _tf);

//                if (!_hasOpen)
//                {
//                    Start(bucketStart, m1);
//                    return false;
//                }

//                if (bucketStart != _startUtc)
//                {
//                    closed = ToBar();
//                    Start(bucketStart, m1);
//                    AppendHistory(closed);
//                    return true;
//                }

//                _h = Math.Max(_h, m1.High);
//                _l = Math.Min(_l, m1.Low);
//                _c = m1.Close;
//                _v += (decimal)m1.Volume;
//                return false;
//            }

//            public IReadOnlyList<TickerBar> GetHistorySnapshot() => _history;

//            /// <summary>
//            /// Preload historical bars into this state (used by PrimeHistory).
//            /// </summary>
//            public void SeedHistory(IEnumerable<TickerBar> history)
//            {
//                foreach (var bar in history.OrderBy(b => b.MinuteUtc))
//                {
//                    AppendHistory(bar);
//                }
//            }

//            private void Start(DateTime start, TickerBar m1)
//            {
//                _startUtc = start;
//                _o = m1.Open;
//                _h = Math.Max(m1.Open, Math.Max(m1.High, m1.Close));
//                _l = Math.Min(m1.Open, Math.Min(m1.Low, m1.Close));
//                _c = m1.Close;
//                _v = (decimal)m1.Volume;
//                _hasOpen = true;
//            }

//            private TickerBar ToBar() => new()
//            {
//                Symbol = _symbol,
//                MinuteUtc = _startUtc,
//                Open = _o,
//                High = _h,
//                Low = _l,
//                Close = _c,
//                Volume = _v,
//                Vwap60s = 0
//            };

//            private void AppendHistory(TickerBar b)
//            {
//                _history.Add(b);
//                if (_history.Count > _cap)
//                    _history.RemoveRange(0, _history.Count - _cap);
//            }

//            private static DateTime Align(DateTime t, TimeSpan tf)
//            {
//                if (tf >= TimeSpan.FromDays(1))
//                {
//                    var days = (int)tf.TotalDays;
//                    var baseDay = new DateTime(t.Year, t.Month, t.Day, 0, 0, 0, DateTimeKind.Utc);
//                    var epochDays = (baseDay - DateTime.UnixEpoch).TotalDays;
//                    var bucket = Math.Floor(epochDays / days) * days;
//                    return DateTime.UnixEpoch.AddDays(bucket);
//                }
//                else
//                {
//                    var ticks = tf.Ticks;
//                    var sinceEpoch = (t - DateTime.UnixEpoch).Ticks;
//                    var floored = (sinceEpoch / ticks) * ticks;
//                    return DateTime.UnixEpoch.AddTicks(floored);
//                }
//            }
//        }
//    }
//}
