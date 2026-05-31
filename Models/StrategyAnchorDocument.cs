using System;
using System.Collections.Generic;

namespace TradingDashboard.Models
{
    public sealed class StrategyAnchorDocument
    {
        public string Code { get; set; } = string.Empty;
        public string Market { get; set; } = "KRX";
        public string BaseDate { get; set; } = string.Empty;
        public bool GateBaseCandleFound { get; set; }
        public int GateBaseCandleOffset { get; set; } = -1;
        public string GateBaseCandleMarket { get; set; } = string.Empty;
        public double GateBaseCandleChangeRate { get; set; }
        public long GateBaseCandleTradeValue { get; set; }
        public long BasePrice { get; set; }
        public string BasePriceDate { get; set; } = string.Empty;
        public string BasePriceSource { get; set; } = string.Empty;
        public string SavedAt { get; set; } = string.Empty;
        public Dictionary<int, StrategyMa60TouchAnchor> Ma60Touches { get; set; } = [];
    }

    public sealed class StrategyMa60TouchAnchor
    {
        public int Minute { get; set; }
        public DateTime Time { get; set; }
        public long TouchPrice { get; set; }
        public double Ma60 { get; set; }
        public long Open { get; set; }
        public long High { get; set; }
        public long Low { get; set; }
        public long Close { get; set; }
        public long Volume { get; set; }
        public long TradingValue { get; set; }
    }
}
