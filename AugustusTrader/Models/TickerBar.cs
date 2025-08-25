using System;

namespace ObserverBot.Models
{
    public sealed class TickerBar
    {
        public string Symbol { get; set; } = "";
        public DateTime MinuteUtc { get; set; }
        public decimal Open { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
        public decimal Close { get; set; }
        public decimal Volume { get; set; }
        public decimal Vwap60s { get; set; }
    }
}
