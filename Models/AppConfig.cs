using System.Collections.Generic;

namespace TradingDashboard.Models
{
    public class AppConfig
    {
        public NaverNewsSettings NaverNews { get; set; } = new NaverNewsSettings();
        public KiwoomSettings Kiwoom { get; set; } = new KiwoomSettings();
        public TelegramSettings Telegram { get; set; } = new TelegramSettings();
        public DashboardSettings Dashboard { get; set; } = new DashboardSettings();
    }

    public class NaverNewsSettings
    {
        public string ClientId { get; set; } = string.Empty;
        public string ClientSecret { get; set; } = string.Empty;
        public int DisplayCount { get; set; } = 5;
        public string Sort { get; set; } = "date";
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
        public List<string> ChatIds { get; set; } = new List<string>();
        public bool Enabled { get; set; } = true;
    }

    public class DashboardSettings
    {
        public int NewsCount { get; set; } = 5;
        public int DisclosureCount { get; set; } = 2;
        public int OrderBookDepth { get; set; } = 10;
        public int RecentTradeCount { get; set; } = 10;
        public int RefreshIntervalMs { get; set; } = 500;
    }
}
