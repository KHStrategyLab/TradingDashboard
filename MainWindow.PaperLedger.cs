namespace TradingDashboard
{
    public partial class MainWindow
    {
        private void LoadPaperPositionLedger()
        {
            _paperPositions.Clear();
            foreach (Models.PaperPositionLedgerEntry entry in _paperPositionLedgerStore.LoadToday())
                _paperPositions.Add(entry);
        }
    }
}
