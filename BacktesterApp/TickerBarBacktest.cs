namespace BacktesterStandalone.Engine
{

    public class TickerBarBacktest
    {
        public DateTime StartTime { get; set; }
        public decimal Open { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
        public decimal Close { get; set; }
        public decimal Volume { get; set; }
        public decimal Vwap60s { get; set; }
    }
}