using System.Collections.Generic;

namespace TradingDashboard.Models
{
    public class AppConfig
    {
        public NaverNewsSettings NaverNews { get; set; } = new NaverNewsSettings();
        public DartSettings Dart { get; set; } = new DartSettings();
        public KiwoomSettings Kiwoom { get; set; } = new KiwoomSettings();
        public TelegramSettings Telegram { get; set; } = new TelegramSettings();
        public DashboardSettings Dashboard { get; set; } = new DashboardSettings();
        public StrategyMinutePreloadSettings StrategyMinutePreload { get; set; } = new StrategyMinutePreloadSettings();
        public LateNewsAlertSettings LateNewsAlert { get; set; } = new LateNewsAlertSettings();
        public TradingCostSettings TradingCosts { get; set; } = new TradingCostSettings();
    }

    public class NaverNewsSettings
    {
        public string ClientId { get; set; } = string.Empty;
        public string ClientSecret { get; set; } = string.Empty;
        public int DisplayCount { get; set; } = 10;
        public int MarketDisplayCount { get; set; } = 20;
        public string MarketQuery { get; set; } = "증권 | 증시 | 코스피 | 코스닥 | 주식 | 금융";
        public string Sort { get; set; } = "date";
    }

    public class DartSettings
    {
        public bool Enabled { get; set; } = true;
        public string ApiKey { get; set; } = string.Empty;
        public int DisplayCount { get; set; } = 2;
        public int LookbackDays { get; set; } = 30;
    }

    public class KiwoomSettings
    {
        public bool UseRestApi { get; set; } = false;
        public bool UseOpenApiPlus { get; set; } = true;
        public string AppKey { get; set; } = string.Empty;
        public string SecretKey { get; set; } = string.Empty;
        public string ConditionSeq01 { get; set; } = "1";
        public string AccountNo { get; set; } = string.Empty;
        public bool AutoLogin { get; set; } = false;
        public bool MockMode { get; set; } = false;
        public bool RealMode { get; set; } = true;
    }

    public class TelegramSettings
    {
        public string BotToken { get; set; } = string.Empty;
        public string DefaultChatId { get; set; } = string.Empty;
        public List<string> ChatIds { get; set; } = [];
        public bool Enabled { get; set; } = true;
    }

    public class DashboardSettings
    {
        public int NewsCount { get; set; } = 10;
        public int DisclosureCount { get; set; } = 2;
        public int OrderBookDepth { get; set; } = 10;
        public int RecentTradeCount { get; set; } = 10;
        public int RefreshIntervalMs { get; set; } = 500;
    }

    public class StrategyMinutePreloadSettings
    {
        public int IdleDelaySeconds { get; set; } = 180;
    }

    public class LateNewsAlertSettings
    {
        public bool Enabled { get; set; } = false;
        public string StartTime { get; set; } = "08:00";
        public string EndTime { get; set; } = "11:00";
        public int WarmupMinutes { get; set; } = 5;
        public int NewsCount { get; set; } = 3;
        public int TitleMaxLength { get; set; } = 25;
    }

    public class TradingCostSettings
    {
        public TradingCostRate Default { get; set; } = new TradingCostRate();
        public TradingCostRate Krx { get; set; } = new TradingCostRate
        {
            CommissionRate = 0.00015m,
            SellTaxRate = 0.0020m
        };
        public TradingCostRate Nxt { get; set; } = new TradingCostRate
        {
            CommissionRate = 0.000145m,
            SellTaxRate = 0.0020m
        };
        public TradingCostRate Sor { get; set; } = new TradingCostRate
        {
            CommissionRate = 0.00015m,
            SellTaxRate = 0.0020m
        };
    }

    public class TradingCostRate
    {
        public decimal CommissionRate { get; set; } = 0.00015m;
        public decimal SellTaxRate { get; set; } = 0.0020m;
    }
}
