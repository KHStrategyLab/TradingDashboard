namespace TradingDashboard.Models
{
    public sealed class InvestorNetBuyMetrics
    {
        public string Code { get; set; } = string.Empty;
        public string Date { get; set; } = string.Empty;
        public long ForeignNetBuyQuantity { get; set; }
        public long InstitutionNetBuyQuantity { get; set; }
        public bool IsForeignInstitutionDoubleNetBuy => ForeignNetBuyQuantity > 0 && InstitutionNetBuyQuantity > 0;
        public string Source { get; set; } = "ka10059";
        public string Unit { get; set; } = "share";
        public bool Found { get; set; }
    }
}
