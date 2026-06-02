namespace TradingDashboard.Models
{
    public sealed class CandidateLedgerEntry
    {
        public string Key { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Market { get; set; } = "KRX";
        public string CandidateTime { get; set; } = string.Empty;
        public string CandidateDate { get; set; } = string.Empty;
        public string ConditionName { get; set; } = string.Empty;
        public string ConditionId { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string StrategyCode { get; set; } = string.Empty;
        public bool NxtEnabled { get; set; }
        public string SorMode { get; set; } = string.Empty;

        public long CurrentPrice { get; set; }
        public decimal ChangeRate { get; set; }
        public long ChangeAmount { get; set; }
        public long OpenPrice { get; set; }
        public long HighPrice { get; set; }
        public long LowPrice { get; set; }
        public long ExpectedClosePrice { get; set; }
        public string PriceUpdatedAt { get; set; } = string.Empty;

        public long TodayOpen { get; set; }
        public long TodayHigh { get; set; }
        public long TodayLow { get; set; }
        public long TodayClose { get; set; }
        public long TodayVolume { get; set; }
        public long TodayTradingValue { get; set; }
        public string TodayBarStatus { get; set; } = "Pending";
        public string TodayBarUpdatedAt { get; set; } = string.Empty;

        public long PrevOpen { get; set; }
        public long PrevHigh { get; set; }
        public long PrevLow { get; set; }
        public long PrevClose { get; set; }
        public long PrevVolume { get; set; }
        public long PrevTradingValue { get; set; }
        public string PrevBarDate { get; set; } = string.Empty;
        public string PrevBarStatus { get; set; } = "Pending";

        public decimal? MarketCap { get; set; }
        public decimal? ListedShares { get; set; }
        public decimal? FloatingShares { get; set; }
        public string FloatingSharesUnit { get; set; } = string.Empty;
        public string MarketCapSource { get; set; } = string.Empty;
        public string ListedSharesSource { get; set; } = string.Empty;
        public string FloatingSharesSource { get; set; } = string.Empty;
        public string FundamentalUpdatedAt { get; set; } = string.Empty;

        public long Volume { get; set; }
        public long TradingValue { get; set; }
        public string VolumeSource { get; set; } = string.Empty;
        public string TradingValueSource { get; set; } = string.Empty;
        public string DataMarket { get; set; } = string.Empty;
        public string PriceMarket { get; set; } = string.Empty;
        public string VolumeMarket { get; set; } = string.Empty;
        public string TradingValueMarket { get; set; } = string.Empty;

        public decimal? ValueToMarketCapPercent { get; set; }
        public decimal? TradingValueToMarketCapPercent { get; set; }
        public decimal? TurnoverRateByFloatingShares { get; set; }
        public decimal? TurnoverRateByListedShares { get; set; }

        public decimal? VolumeVsPrevDayRatioPercent { get; set; }
        public decimal? VolumeVsPrevDayIncreasePercent { get; set; }
        public decimal? TradingValueVsPrevDayRatioPercent { get; set; }
        public decimal? TradingValueVsPrevDayIncreasePercent { get; set; }

        public decimal? AvgVolume20D { get; set; }
        public decimal? AvgTradingValue20D { get; set; }
        public decimal? AvgRange20D { get; set; }
        public decimal? AvgChangeRate20D { get; set; }
        public int DailyBarsLoadedCount { get; set; }
        public string DailyMetricsStatus { get; set; } = "Pending";
        public string DailyMetricsUpdatedAt { get; set; } = string.Empty;
        public decimal? VolumeToAvg20RatioPercent { get; set; }
        public decimal? VolumeIncreaseVsAvg20Percent { get; set; }
        public decimal? TradingValueToAvg20RatioPercent { get; set; }
        public decimal? TradingValueIncreaseVsAvg20Percent { get; set; }

        public bool? IsBullishCandle { get; set; }
        public decimal? CandleRangePercent { get; set; }
        public decimal? BodyPercent { get; set; }
        public decimal? UpperTailPercent { get; set; }
        public decimal? LowerTailPercent { get; set; }
        public decimal? CloseLocationPercent { get; set; }
        public bool? IsLimitUpLike { get; set; }
        public bool? IsBollingerUpperBreak { get; set; }
        public decimal? BollingerUpper20 { get; set; }

        public bool? PrevHighBreak { get; set; }
        public decimal? DistanceFromPrevHighPercent { get; set; }
        public bool? TodayCloseAbovePrevHigh { get; set; }
        public bool? TodayLowAbovePrevHigh { get; set; }
        public decimal? PrevCloseGapPercent { get; set; }

        public string MarketCapClass { get; set; } = string.Empty;
        public int? TradingValueRankInMarket { get; set; }
        public int? ChangeRateRankInMarket { get; set; }
        public int? TurnoverRankInMarket { get; set; }
        public string SectorName { get; set; } = string.Empty;
        public string ThemeName { get; set; } = string.Empty;
        public int? SectorTradingValueRank { get; set; }
        public int? SectorChangeRateRank { get; set; }
        public bool? IsLargeCapEventCandidate { get; set; }

        public string FundamentalStatus { get; set; } = "Pending";
        public string MarketLogicStatus { get; set; } = "Pending";
        public string ScoreStatus { get; set; } = "Pending";
        public string DataErrorMemo { get; set; } = string.Empty;
        public string MissingFieldMemo { get; set; } = string.Empty;

        public decimal? SelectionScore { get; set; }
        public decimal? MarketLogicScore { get; set; }
        public decimal? BaseCandleQualityScore { get; set; }
        public decimal? LeaderScore { get; set; }
        public decimal? LiquidityScore { get; set; }
        public decimal? TurnoverScore { get; set; }
        public string ScoreMemo { get; set; } = string.Empty;
        public string ScoreUpdatedAt { get; set; } = string.Empty;

        public string Status { get; set; } = "Active";
        public string SavedAt { get; set; } = string.Empty;
        public string UpdatedAt { get; set; } = string.Empty;
    }
}
