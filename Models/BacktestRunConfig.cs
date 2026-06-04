using System;
using System.Collections.Generic;

namespace TradingDashboard.Models
{
    public sealed class BacktestRunConfig
    {
        public string RunId { get; set; } = string.Empty;
        public string BacktestMode { get; set; } = "SOR_ON";
        public string RunMode { get; set; } = string.Empty;
        public string MarketMode { get; set; } = string.Empty;
        public string OrderMode { get; set; } = "None";
        public bool LiveOrder { get; set; }
        public string ExecutionType { get; set; } = "BacktestOnly";
        public string DataStorePolicy { get; set; } = "ReuseOnly";
        public string DataStorePath { get; set; } = "Storage/Backtests/DataStore";
        public string CreatedAt { get; set; } = DateTime.Now.ToString("yyyyMMddHHmmss");
        public List<string> StrategyCodes { get; set; } = [];
        public List<string> ExitRuleCodes { get; set; } = [];
        public string Memo { get; set; } = "DataStore is not copied into Runs. No live orders.";
    }
}
